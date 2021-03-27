using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaToolkit;
using MediaToolkit.Model;
using MediaToolkit.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AzureFaceCog.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private static IFaceClient client;
          private readonly double _headPitchMaxThreshold = 30;

        private readonly double _headPitchMinThreshold = -15;

        private readonly double _headYawMaxThreshold = 20;

        private readonly double _headYawMinThreshold = -20;

        private readonly double _headRollMaxThreshold = 20;

        private readonly double _headRollMinThreshold = -20;
        private static bool processIdel = true;

        private static bool firstInProcess = true;

        private static int processStep = 1;

        private static List<double> buff = new List<double>();

        private readonly static int activeFrames = 14;

        private readonly IHostingEnvironment _env;

        public WeatherForecastController(IHostingEnvironment env)
        {
            _env = env;
            client = new FaceClient(new ApiKeyServiceClientCredentials("***********"))
            {
                Endpoint = "https://eastus.api.cognitive.microsoft.com"
            };
        }

        [HttpGet]
        public IEnumerable<WeatherForecast> Get()
        {
            var rng = new Random();
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = rng.Next(-20, 55),
                Summary = Summaries[rng.Next(Summaries.Length)]
            })
            .ToArray();
        }

        [HttpPost]
        public async Task<IActionResult> ProcessVideoFile([FromBody] ImageRequest req)
        {
            try
            {
                var fileName = "test.mp4";
                byte[] imageBytes = Convert.FromBase64String(req.ImageFile);

                string FilePath = Path.Combine(Directory.GetCurrentDirectory(), "files");

                if (!System.IO.File.Exists(FilePath))
                {
                    if (!Directory.Exists(FilePath))
                    {
                        Directory.CreateDirectory(FilePath);
                    }
                    System.IO.File.WriteAllBytes(Path.Combine(FilePath, fileName), imageBytes);
                    FileStream s2 = new FileStream(Path.Combine(FilePath, fileName), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                }
                //Extract
                ExtractFrameFromVideo(FilePath, fileName);

                //Convert Image to stream
                var headPose = await ConvertImagesToMemoryStream(FilePath);

                return Ok("Success");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return BadRequest(ex.Message);
            }
        }

        private void ExtractFrameFromVideo(string directory, string fiileName)
        {
            var mp4 = new MediaFile { Filename = Path.Combine(directory, fiileName) };
            using var engine = new Engine();


            engine.GetMetadata(mp4);

            var i = 0;
            while (i < mp4.Metadata.Duration.Seconds)
            {
                var options = new ConversionOptions { Seek = TimeSpan.FromSeconds(i) };
                var outputFile = new MediaFile { Filename = string.Format("{0}\\image-{1}.jpeg", Path.Combine(directory), i) };
                engine.GetThumbnail(mp4, outputFile, options);
                i++;
            }
        }

        private async Task<HeadPose> ConvertImagesToMemoryStream(string filePath)
        {
            var files = Directory.GetFiles(filePath);
            foreach (var item in files)
            {
                if (item.EndsWith("mp4"))
                {
                    continue;
                }
                //MemoryStream ms = new MemoryStream();
                //using (FileStream file = new FileStream(item, FileMode.Open, FileAccess.Read))
                //    file.CopyTo(ms);

                FileStream fileStream = new FileStream(item, FileMode.Open);
                Image image = Image.FromStream(fileStream);
                MemoryStream memoryStream = new MemoryStream();
                image.Save(memoryStream, ImageFormat.Jpeg);
                //Close File Stream
                fileStream.Close();

                var fileName = item.Split('\\').Last();

                var imageUrl = Path.Combine(_env.ContentRootPath, $"files/{fileName}");

                // Submit image to API. 
                var attrs = new List<FaceAttributeType> { FaceAttributeType.HeadPose };

                var faces = await client.Face.DetectWithUrlWithHttpMessagesAsync("https://images.generated.photos/144AF0RRO5TihRwAcxlCIjnJrUiUlAhCoMuVlhNiZMQ/rs:fit:256:256/Z3M6Ly9nZW5lcmF0/ZWQtcGhvdG9zL3Yz/XzAxNDcwMjIuanBn.jpg", returnFaceId: false, returnFaceAttributes: attrs);
                var headPose = faces.Body.First().FaceAttributes?.HeadPose;

                
                processStep = 1;
                Doprocess(headPose);


                // Output. 
                return headPose;
            }
            return null;
        }


        private void Doprocess(HeadPose headPose)
        {
            processIdel = false;

            var pitch = headPose.Pitch;
            var roll = headPose.Roll;
            var yaw = headPose.Yaw;

            switch (processStep)
            {
                case 1:
                    if (firstInProcess)
                    {
                        firstInProcess = false;
                     //   Console.WriteLine("Step1: detect head pose up and down.");
                    //    IndicateMsg = "Please look Up and Down!";
                    }

                    StepOne(pitch);
                    break;
                case 2:
                    if (firstInProcess)
                    {
                        firstInProcess = false;
                     //   Console.WriteLine("Step2: detect head pose Left and Right.");
                    //    IndicateMsg = "Please look Left and Right!";
                    }

                    StepTwo(yaw);
                    break;
                case 3:
                    if (firstInProcess)
                    {
                        firstInProcess = false;
                     //   Console.WriteLine("Step3: detect head pose roll left and Right.");
                     //   IndicateMsg = "Please roll you face Left and Right!";
                    }

                    StepThree(roll);
                    break;
                default:
                    break;
            }
        }

        private void StepOne(double pitch)
        {
            buff.Add(pitch);
            if (buff.Count > activeFrames)
            {
                buff.RemoveAt(0);
            }

            var max = buff.Max();
            var min = buff.Min();

            var maxCorrection = max < 0 ? 0 : Convert.ToInt32(max);
            var minCorrection = min > 0 ? 0 : Convert.ToInt32(Math.Abs(min));

            // MsgProcessVerticalTop = GetVerticalTopProgressBarString(maxCorrection);
            // MsgProcessVerticalDown = GetVerticalDownProgressBarString(minCorrection);

            if (max > _headPitchMaxThreshold && min < _headPitchMinThreshold)
            {
              //  IndicateMsg = "Nodding Detected!";
                Console.WriteLine("Nodding Detected success.");
               // CleanBuffAndSetToStep(2);
                Wait2SecondsToReleaseProcess();
            }
            else
            {
                processIdel = true;
            }
        }

        private void StepTwo(double yaw)
        {
            buff.Add(yaw);
            if (buff.Count > activeFrames)
            {
                buff.RemoveAt(0);
            }

            var max = buff.Max();
            var min = buff.Min();

            var maxCorrection = max < 0 ? 0 : Convert.ToInt32(max * 2);
            var minCorrection = min > 0 ? 0 : Convert.ToInt32(Math.Abs(min * 2));

         //   MsgProcessHorizontalLeft = GetHorizontalLeftProgressBarString(maxCorrection);
          //  MsgProcessHorizontalRight = GetHorizontalRightProgressBarString(minCorrection);

            if (min < _headYawMinThreshold && max > _headYawMaxThreshold)
            {
              //  CleanBuffAndSetToStep(3);
             //   IndicateMsg = "Shaking Detected!";
                Console.WriteLine("Shaking Detected success.");
                Wait2SecondsToReleaseProcess();
            }
            else
            {
                processIdel = true;
            }
        }

        private void StepThree(double roll)
        {
            buff.Add(roll);
            if (buff.Count > activeFrames)
            {
                buff.RemoveAt(0);
            }

            var max = buff.Max();
            var min = buff.Min();

            var maxCorrection = max < 0 ? 0 : Convert.ToInt32(max * 2);
            var minCorrection = min > 0 ? 0 : Convert.ToInt32(Math.Abs(min * 2));

          //  MsgProcessHorizontalLeft = GetHorizontalLeftProgressBarString(maxCorrection);
           // MsgProcessHorizontalRight = GetHorizontalRightProgressBarString(minCorrection);

            if (min < _headRollMinThreshold && max > _headRollMaxThreshold)
            {
              //  StopProcess();
               // IndicateMsg = "Rolling Detected!";
                Console.WriteLine("Rolling Detected success.");
                Console.WriteLine("All head pose detection finished.");
                Wait2SecondsToReleaseProcess();
            }
            else
            {
                processIdel = true;
            }
        }

        private void CleanBuffAndSetToStep(int step)
        {
            buff = new List<double>();
            firstInProcess = true;
            processStep = step;
        }

        private void Wait2SecondsToReleaseProcess()
        {
            new Task(
                () =>
                {
                    Thread.Sleep(2000);
                    processIdel = true;
                }).Start();
        }

    }
}


public class ImageRequest
{
    public string ImageFile { get; set; }
}

public class LiveCameraResult
{
    public DetectedFace[] Faces { get; set; } = null;
}




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


        private readonly static int activeFrames = 14;

        private readonly IHostingEnvironment _env;

        public WeatherForecastController(IHostingEnvironment env)
        {
            _env = env;
            client = new FaceClient(new ApiKeyServiceClientCredentials("**************************"))
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
        public async Task<IActionResult> ProcessVideoFile([FromBody] ImageRequest req, [FromQuery] int process = 1)
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
                        System.IO.File.WriteAllBytes(Path.Combine(FilePath, fileName), imageBytes);
                    }
                }
                //Extract
                ExtractFrameFromVideo(FilePath, fileName);

                //Convert Image to stream
                var headPoseResult = await RunHeadGestureOnImageFrame(FilePath, process);
                return Ok(headPoseResult);
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

        private async Task<string> RunHeadGestureOnImageFrame(string filePath, int process)
        {
            var headGestureResult = "";
            var buff = new List<double>();

            var files = Directory.GetFiles(filePath);
            foreach (var item in files)
            {
                if (item.EndsWith("mp4"))
                {
                    continue;
                }
                var fileName = item.Split('\\').Last();
                var imageName = fileName.Split('.').First();

                //UPLOAD IMAGE TO FIREBASE 
                // var baseString = GetBaseStringFromImagePath(item);
                byte[] imageArray = System.IO.File.ReadAllBytes(item);
                var uploadedContent = await FireBase.UploadDocumentAsync(fileName, imageName, item);

                // Submit image to API. 
                var attrs = new List<FaceAttributeType> { FaceAttributeType.HeadPose };

                //TODO: USE IMAGE URL OF NETWORK
                var faces = await client.Face.DetectWithUrlWithHttpMessagesAsync(uploadedContent, returnFaceId: false, returnFaceAttributes: attrs);
                var headPose = faces.Body.First().FaceAttributes?.HeadPose;

                var pitch = headPose.Pitch;
                var roll = headPose.Roll;
                var yaw = headPose.Yaw;


                if (process == 1)
                {
                    headGestureResult = StepOne(buff, pitch);

                    if (!string.IsNullOrEmpty(headGestureResult))
                        return headGestureResult;
                }

                if (process == 2)
                {
                    headGestureResult = StepTwo(buff, yaw);
                    if (!string.IsNullOrEmpty(headGestureResult))
                        return headGestureResult;
                }

                if (process == 3)
                {
                    headGestureResult = StepThree(buff, roll);
                    if (!string.IsNullOrEmpty(headGestureResult))
                        return headGestureResult;
                }
            }
            return "Head Gesture not completed";
        }

        private string StepOne(List<double> buff, double pitch)
        {
            buff.Add(pitch);
            if (buff.Count > activeFrames)
            {
                buff.RemoveAt(0);
            }

            var max = buff.Max();
            var min = buff.Min();

            if (max > _headPitchMaxThreshold && min < _headPitchMinThreshold)
            {
                return "Nodding Detected success.";
            }
            else
            {
                return null;
            }
        }

        private string StepTwo(List<double> buff, double yaw)
        {
            buff.Add(yaw);
            if (buff.Count > activeFrames)
            {
                buff.RemoveAt(0);
            }

            var max = buff.Max();
            var min = buff.Min();

            if (min < _headYawMinThreshold && max > _headYawMaxThreshold)
            {
                return "Shaking Detected success.";
            }
            else
            {
                return null;
            }
        }

        private string StepThree(List<double> buff, double roll)
        {
            buff.Add(roll);
            if (buff.Count > activeFrames)
            {
                buff.RemoveAt(0);
            }

            var max = buff.Max();
            var min = buff.Min();

            if (min < _headRollMinThreshold && max > _headRollMaxThreshold)
            {
                Console.WriteLine("All head pose detection finished.");
                return "Rolling Detected success.";
            }
            else
            {
                return null;
            }
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




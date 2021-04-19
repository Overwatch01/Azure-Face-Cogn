using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediaToolkit;
using MediaToolkit.Model;
using MediaToolkit.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Microsoft.Extensions.Hosting;

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
        private readonly double _headPitchMaxThreshold = 9; //25

        private readonly double _headPitchMinThreshold = -9; //-15

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
            client = new FaceClient(new ApiKeyServiceClientCredentials("e8ef40efa4704769860e661c210a0fc5"))
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

                string FilePath = Path.Combine(Directory.GetCurrentDirectory(), "filess");

                if (!System.IO.File.Exists(FilePath))
                {
                    if (!Directory.Exists(FilePath))
                    {
                        Directory.CreateDirectory(FilePath);
                        System.IO.File.WriteAllBytes(Path.Combine(FilePath, fileName), imageBytes);
                    }
                }
                // Extract
                  ExtractFrameFromVideo(FilePath, fileName);

                //Convert Image to stream
                var headPoseResult = await RunHeadGestureOnImageFrame(FilePath);
                var response = new Response
                {
                    HeadNodingDetected = headPoseResult.Item1,
                    HeadShakingDetected = headPoseResult.Item2,
                    HeadRollingDetected = headPoseResult.Item3
                };
                return Ok(response);
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
                var options = new ConversionOptions { Seek = TimeSpan.FromSeconds(i),  };
                var outputFile = new MediaFile { Filename = string.Format("{0}\\image-{1}.jpeg", Path.Combine(directory), i) };
                engine.GetThumbnail(mp4, outputFile, options);
                i++;
            }
        }

        private async Task<Tuple<bool, bool, bool>> RunHeadGestureOnImageFrame(string filePath)
        {
            var headGestureResult = "";
            bool runStepOne = true;
            bool runStepTwo = true;
            bool runStepThree = true;
            bool stepOneComplete = false;
            bool stepTwoComplete = false;
            bool stepThreeComplete = false;

            var buffPitch = new List<double>();
            var buffYaw = new List<double>();
            var buffRoll = new List<double>();

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
                if (faces.Body.Count <= 0)
                {
                    continue;
                }
                var headPose = faces.Body.First().FaceAttributes?.HeadPose;

                var pitch = headPose.Pitch;
                var roll = headPose.Roll;
                var yaw = headPose.Yaw;


                if (runStepOne)
                {
                    headGestureResult = StepOne(buffPitch, pitch);
                    if (!string.IsNullOrEmpty(headGestureResult))
                    {
                        runStepOne = false;
                        stepOneComplete = true;
                    }
                }

                if (runStepTwo)
                {
                    headGestureResult = StepTwo(buffYaw, yaw);
                    if (!string.IsNullOrEmpty(headGestureResult))
                    {
                        runStepTwo = false;
                        stepTwoComplete = true;
                    }
                }

                if (runStepThree)
                {
                    headGestureResult = StepThree(buffRoll, roll);
                    if (!string.IsNullOrEmpty(headGestureResult))
                    {
                        runStepThree = false;
                        stepThreeComplete = true;
                    }
                        
                }
            }
            return new Tuple<bool, bool, bool>(stepOneComplete, stepTwoComplete, stepThreeComplete);
        }

        private string StepOne(List<double> buffPitch, double pitch)
        {
            buffPitch.Add(pitch);
            if (buffPitch.Count > activeFrames)
            {
                buffPitch.RemoveAt(0);
            }

            var max = buffPitch.Max();
            var min = buffPitch.Min();

            if (min < _headPitchMinThreshold && max > _headPitchMaxThreshold)
            {
                return "Nodding Detected success.";
            }
            else
            {
                return null;
            }
        }

        private string StepTwo(List<double> buffYaw, double yaw)
        {
            buffYaw.Add(yaw);
            if (buffYaw.Count > activeFrames)
            {
                buffYaw.RemoveAt(0);
            }

            var max = buffYaw.Max();
            var min = buffYaw.Min();

            if (min < _headYawMinThreshold && max > _headYawMaxThreshold)
            {
                return "Shaking Detected success.";
            }
            else
            {
                return null;
            }
        }

        private string StepThree(List<double> buffRoll, double roll)
        {
            buffRoll.Add(roll);
            if (buffRoll.Count > activeFrames)
            {
                buffRoll.RemoveAt(0);
            }

            var max = buffRoll.Max();
            var min = buffRoll.Min();

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


public class Response
{
    public bool HeadNodingDetected { get; set; }
    public bool HeadShakingDetected { get; set; }
    public bool HeadRollingDetected { get; set; }
}



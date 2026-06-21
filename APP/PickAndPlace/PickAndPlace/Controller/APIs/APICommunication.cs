using PickAndPlace.Models;
using Emgu.CV;
using Emgu.CV.Structure;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.IO;

namespace PickAndPlace.Controllers.APIs
{
    class APICommunication
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public static Properties.Settings _param = Properties.Settings.Default;


        public static GetCoordResponse GetRealCoord(string url, Image<Bgr, byte> image, int timeout = 15000)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };

            var client = new RestClient(options);

            var request = new RestRequest(_param.EndPointGetRealCoord, Method.Post);
            request.AlwaysMultipartFormData = true;
            // Add File
            byte[] jpegData = image.ToJpegData();
            request.AddFile("image", jpegData, $"image.jpg");

            var response = client.Execute(request);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {
                    return JsonConvert.DeserializeObject<GetCoordResponse>(response.Content);
                }
                catch (Exception ex)
                {
                    logger.Debug(ex.Message);
                    return null;
                }
            }

            return null;
        }

        public static Calib2DResponse Calibration2D(string url, ObservableCollection<PairPoint> pairPoints, int timeout = 10000)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };

            var client = new RestClient(options);

            var request = new RestRequest(_param.EndPointCalib2d, Method.Post);

            var payload = new
            {
                pixel_points = pairPoints.Select(p => new[] { p.ImagePixel.Item1, p.ImagePixel.Item2 }).ToList(),
                robot_points = pairPoints.Select(p => new[] { p.RobotCoord.Item1, p.RobotCoord.Item2 }).ToList()
            };


            //var demePairPoints = new ObservableCollection<PairPoint>();
            //demePairPoints.Add(new PairPoint(0, new Tuple<double, double>(812.3, 642.8), new Tuple<double, double>(305.2, 118.6)));
            //demePairPoints.Add(new PairPoint(1, new Tuple<double, double>(1432.7, 610.5), new Tuple<double, double>(530.8, 132.4)));
            //demePairPoints.Add(new PairPoint(2, new Tuple<double, double>(1470.1, 1032.4), new Tuple<double, double>(542.1, 282.7)));
            //demePairPoints.Add(new PairPoint(3, new Tuple<double, double>(845.6, 1065.2), new Tuple<double, double>(316.4, 268.3)));
            
            //var payload = new
            //{
            //    pixel_points = demePairPoints.Select(p => new[] { p.ImagePixel.Item1, p.ImagePixel.Item2 }).ToList(),
            //    robot_points = demePairPoints.Select(p => new[] { p.RobotCoord.Item1, p.RobotCoord.Item2 }).ToList()
            //};

            request.AddJsonBody(payload);

            var response = client.Execute(request);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {
                    return JsonConvert.DeserializeObject<Calib2DResponse>(response.Content);
                }
                catch (Exception ex)
                {
                    logger.Debug(ex.Message);
                    return null;
                }
            }

            return null;
        }
        public static Calib2DResponse TransformPoint(string url, double x, double y, int timeout = 10000)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };

            var client = new RestClient(options);

            var request = new RestRequest(_param.EndPointTransform, Method.Post);

            var payload = new
            {
                pixel = new[] { x, y }
            };

            request.AddJsonBody(payload);

            var response = client.Execute(request);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {
                    return JsonConvert.DeserializeObject<Calib2DResponse>(response.Content);
                }
                catch (Exception ex)
                {
                    logger.Debug(ex.Message);
                    return null;
                }
            }

            return null;
        }
        public static Calib2DResponse SaveMatrix(string url, int timeout = 10000)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };

            var client = new RestClient(options);

            var request = new RestRequest(_param.EndPointSaveMatrix, Method.Post);

            var response = client.Execute(request);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {
                    return JsonConvert.DeserializeObject<Calib2DResponse>(response.Content);
                }
                catch (Exception ex)
                {
                    logger.Debug(ex.Message);
                    return null;
                }
            }

            return null;
        }
        public static Calib2DResponse LoadExistingMatrix(string url, int timeout = 10000)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };

            var client = new RestClient(options);

            var request = new RestRequest(_param.EndPointLoadMatrix, Method.Post);

            var response = client.Execute(request);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {
                    return JsonConvert.DeserializeObject<Calib2DResponse>(response.Content);
                }
                catch (Exception ex)
                {
                    logger.Debug(ex.Message);
                    return null;
                }
            }

            return null;
        }
        public static bool CheckMatrixReady(string url, int timeout = 10000)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };

            var client = new RestClient(options);

            var request = new RestRequest(_param.EndPointCheckCalibReady, Method.Post);

            var response = client.Execute(request);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {
                    var res = JsonConvert.DeserializeObject<Calib2DResponse>(response.Content);
                    if (res != null && res.Result)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug(ex.Message);
                    return false;
                }
            }

            return false;
        }

        public static bool CheckAPIStatus(string url, int timeout = 1000)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndPointCheckStatus, Method.Get);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        internal static bool LoadTemplates(
     string url,
     List<Image<Bgr, byte>> imageList,
     List<List<double>> offsets,
     int timeout = 2000)
        {
            try
            {
                var options = new RestClientOptions(url)
                {
                    Timeout = TimeSpan.FromMilliseconds(timeout)
                };

                var client = new RestClient(options);

                var request = new RestRequest(_param.EndPointLoadTemplates, Method.Post);
                request.AlwaysMultipartFormData = true;

                int index = 0;

                // Add images
                foreach (var img in imageList)
                {
                    byte[] jpegData = img.ToJpegData();

                    request.AddFile(
                        "images",
                        jpegData,
                        $"template_{index}.jpg",
                        "image/jpeg"
                    );

                    index++;
                }

                // Add offsets as multipart form field
                string offsetsJson = JsonConvert.SerializeObject(offsets);

                request.AddParameter(
                    "offsets",
                    offsetsJson,
                    ParameterType.GetOrPost
                );

                var response = client.Execute(request);

                // In ra lỗi để debug 422
                logger.Debug($"StatusCode: {response.StatusCode}");
                logger.Debug($"ResponseContent: {response.Content}");

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    try
                    {
                        return JsonConvert.DeserializeObject<GetCoordResponse>(response.Content).Result;
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex.Message);
                        return false;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.Debug(ex.Message);
                return false;
            }
        }
    }
}

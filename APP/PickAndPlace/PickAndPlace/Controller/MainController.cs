using DiskInspection.Controllers;
using Emgu.CV;
using Emgu.CV.Structure;
using PickAndPlace.Controller.Robot;
using PickAndPlace.Controllers;
using PickAndPlace.Controllers.APIs;
using PickAndPlace.Controllers.Camera;
using PickAndPlace.Models;
using PickAndPlace.Security;
using PickAndPlace.Utils;
using PickAndPlace.Views;
using PickAndPlace.Views.ActivationWindows;
using PickAndPlace.Views.UtilitiesWindows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace PickAndPlace.Controller
{
    class MainController
    {
        private MainWindow _mainWindow;
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private Properties.Settings _param = Properties.Settings.Default;

        public bool _serviceIsRun = false;
        private bool _ForceStopProcess;
        private ModelInfo _model;
        private bool _isRunning;
        private CancellationTokenSource _inspectCts;

        private CameraManager _cameraManager;
        private LincolnCamera _camera;
        private DobotRobotClient _robot;

        public MainController(MainWindow window)
        {
            _mainWindow = window;

        }
        public async Task<bool> RunServiceAsync(int timeout, string content)
        {
            _mainWindow.SetLoadingService(content);

            _logger.Info("Start Service");
            AppLogger.Instance.Info("Loading Program...", "SYSTEM");

            AIServiceController.CloseProcessExisting();
            AIServiceController.Start();

            int timeStep = timeout / 1000;

            for (int i = 0; i < timeStep; i++)
            {
                await Task.Delay(1000);

                if (APICommunication.CheckAPIStatus(_param.ApiUrlAi, 200))
                {
                    _logger.Info("Start AI Engine Successfully!");
                    AppLogger.Instance.Info("Loaded Program Successfully!", "SYSTEM");

                    _serviceIsRun = true;
                    return true;
                }
            }

            return false;
        }

        internal async Task<bool> StartAsync(ModelInfo model)
        {
            try
            {
                _logger.Info("Starting inspection...");
                _ForceStopProcess = false;

                bool cameraOk = CheckAndStartCamera();
                if (!cameraOk)
                {
                    AppLogger.Instance.Error("Camera is not ready, Stop inspection...", "SYSTEM");
                    return false;
                }

                bool robotOk = await CheckAndStartRobotAsync();
                if (!robotOk)
                {
                    AppLogger.Instance.Error("Robot is not ready, Stop inspection...", "SYSTEM");
                    return false;
                }

                bool engineOk = await CheckAndStartEngineAsync();
                if (!engineOk)
                {
                    AppLogger.Instance.Error("Engine is not ready, Stop inspection...", "SYSTEM");
                    return false;
                }

                _logger.Debug("Camera, Robot and Engine are ready, Ready for detection...");
                AppLogger.Instance.Info("Camera, Robot and Engine are ready, Ready for detection...", "SYSTEM");

                _model = model;

                bool loadRes = LoadTemplatesToEngine(_model.Templates);
                if (!loadRes)
                {
                    _logger.Error("Load Templates to Engine failed, Stop inspection...");
                    AppLogger.Instance.Error("Load Templates to Engine failed, Stop inspection...", "SYSTEM");
                    return false;
                }

                _isRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                AppLogger.Instance.Error(ex.Message, "SYSTEM_START_FAILED");
                return false;
            }
        }

        private bool LoadTemplatesToEngine(List<Template> templates)
        {
            var imageList = templates.Select(x => x.Image).ToList();
            return APICommunication.LoadTemplates(_param.ApiUrlAi, imageList);
        }

        internal void Stop()
        {
            if (_camera != null)
            {
                _camera.Stop();
            }
        }
        internal void Close()
        {
            if (_camera != null)
            {
                _camera.Stop();
                _camera.Close();
            }
            if (_robot != null)
            {
                _robot.Dispose();
            }
            AIServiceController.CloseProcessExisting();
        }

        private async Task<bool> CheckAndStartEngineAsync()
        {
            if (!APICommunication.CheckAPIStatus(_param.ApiUrlAi))
            {
                var res = _mainWindow.ShowWarning(
                    "Engine is not running, proceed to restart?\nAI engine đang không chạy, bạn muốn khởi động lại AI engine?!"
                );

                bool resRestart = await RunServiceAsync(20000, "Restarting AI engine...");

                if (!resRestart)
                {
                    _mainWindow.ShowError(
                        "Restart AI engine fail, please contact the vendor!\r AI engine khởi động thất bại, hãy liên hệ với vendor!"
                    );

                    return false;
                }
            }

            if (!APICommunication.CheckMatrixReady(_param.ApiUrlAi))
            {
                _mainWindow.ShowError(
                    "Calibration is not ready, please process calibration!\nChưa thực hiện Calibration, hãy tiến hành Calibration!"
                );

                return false;
            }

            return true;
        }

        private async Task<bool> CheckAndStartRobotAsync()
        {
            try
            {
                if (_robot != null && _robot.IsConnected())
                {
                    return true;
                }

                _robot?.Dispose();
                _robot = null;

                var robot = new DobotRobotClient(
                    _param.RobotIp,
                    _param.RobotPort,
                    timeoutMs: _param.WriteTimeout
                );

                bool connected = await robot.ConnectAsync();

                if (!connected || !robot.IsConnected())
                {
                    robot.Dispose();

                    AppLogger.Instance.Error(
                        "Robot is not connected, login failed!",
                        "ROBOT_CONNECT_FAILED"
                    );

                    return false;
                }

                _robot = robot;
                AppLogger.Instance.Info("Robot connected successfully.", "ROBOT");

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex.Message, "ROBOT_CONNECT_FAILED");

                _robot?.Dispose();
                _robot = null;

                return false;
            }
        }

        private bool CheckAndStartCamera()
        {
            //return true;
            _cameraManager = CameraManager.GetInstance();
            _camera = _cameraManager.GetCamera();
            if (!_camera.IsOpen())
            {
                _mainWindow.ShowError(string.Format("Không mở được camera với SN {0}\nCan't open  camera with SN:{0}", _param.CamSn));
                AppLogger.Instance.Error(string.Format("Không mở được camera với SN {0}\nCan't open  camera with SN:{0}", _param.CamSn), "CAMERA_OPEN_FAILED");
                return false;
            }
            _camera.SetWorkMode(CameraWorkMode.SoftwareTrigger);
            _camera.SetExposureTime(_param.CamExposure);
            _camera.Start();
            return true;
        }

        internal async Task ProcessImageAsync(ModelInfo model)
        {
            if (_camera == null)
            {
                AppLogger.Instance.Error("Camera is null.", "CAMERA_ERROR");
                return;
            }

            if (_robot == null || !_robot.IsConnected())
            {
                AppLogger.Instance.Error("Robot is not connected.", "ROBOT_ERROR");
                return;
            }

            var bitmap = _camera.TriggerAndGetFrame();

            AppLogger.Instance.Info("DONE Capturing Image", "SYSTEM");
            _mainWindow.UpdateImage(bitmap);

            var emguCvImage = new Image<Bgr, byte>(bitmap);

            var res = APICommunication.GetRealCoord(
                _param.ApiUrlAi,
                emguCvImage,
                model.Width,
                model.Height
            );

            if (res == null)
            {
                AppLogger.Instance.Error("INTERNAL ERROR: Cannot Calculate Real Coordinates", "SYSTEM");
                return;
            }

            if (!res.Result)
            {
                AppLogger.Instance.Error("ERROR: Cannot Find The Matching PCB Corner", "SYSTEM");
                _mainWindow.UpdateStatistics(false);
                _mainWindow.UpdateInspectionStatus(false);
                return;
            }

            AppLogger.Instance.Info("DONE: Calculating Real Coordinates", "SYSTEM");

            _mainWindow.UpdateImage(Converter.Base64ToBitmap(res.ResImg));

            _mainWindow.UpdateCalculateResult(
                (double)res.Score,
                (double)res.ImageX,
                (double)res.ImageY,
                (double)res.ImageAngle,
                (double)res.RobotX,
                (double)res.RobotY,
                (double)res.RobotAngle
            );

            bool pickOk = await _robot.PickAsync(
                (double)res.RobotX,
                (double)res.RobotY,
                (double)res.RobotAngle
            );

            if (!pickOk)
            {
                AppLogger.Instance.Error(
                    $"Pick command failed. X: {res.RobotX}, Y: {res.RobotY}, Angle: {res.RobotAngle}",
                    "ROBOT_PICK_FAILED"
                );

                _mainWindow.UpdateStatistics(false);
                _mainWindow.UpdateInspectionStatus(false);
                return;
            }

            AppLogger.Instance.Info(
                $"Sent Pick Command X: {res.RobotX} Y: {res.RobotY} Angle: {res.RobotAngle}",
                "SYSTEM"
            );

            _mainWindow.UpdateStatistics(true);
            _mainWindow.UpdateInspectionStatus(true);
        }

        internal bool CheckLicense()
        {
            string licensePath = @"plugin\license.dat";
            var error = "License is not valid, contact with vendor to active!\rLicense không hợp lệ, liên hệ với vendor để active!";
            var info = "Activation key is valid, continue to use!\rActivation key hợp lệ, hãy tiếp tục sử dụng chương trình!";
            var res = false;
            if (!File.Exists(licensePath))
            {
                _mainWindow.Dispatcher.Invoke(() => 
                {
                    var win = new ActivationWindow();
                    win.Topmost = true;

                    if (win.ShowDialog() != true)
                    {
                        AppLogger.Instance.Error("License is not valid!", "SYSTEM");
                        _mainWindow.ShowError(error);
                    }
                    else
                    {
                        AppLogger.Instance.Info("License is valid!", "SYSTEM");
                        _mainWindow.ShowInfo(info);
                        res = true;
                    }
                });
            }
            else
            {
                string key = File.ReadAllText(licensePath);
                (bool isValid, string message) = LicenseManager.ValidateActivationKey(key);
                if (!isValid)
                {
                    AppLogger.Instance.Error(message, "SYSTEM");
                    _mainWindow.ShowError(error);

                    AppLogger.Instance.Info("Processing creating new activation key!", "SYSTEM");

                    _mainWindow.Dispatcher.Invoke(() => 
                    {
                        var win = new ActivationWindow();
                        win.Topmost = true;
                        if (win.ShowDialog() != true)
                        {
                            AppLogger.Instance.Error("License is not valid!", "SYSTEM");
                            _mainWindow.ShowError(error);
                        }

                        else
                        {
                            AppLogger.Instance.Info("License is valid!", "SYSTEM");
                            _mainWindow.ShowInfo(info);
                            res = true;
                        }
                    });
                }
                else
                {
                    AppLogger.Instance.Info("License is valid!", "SYSTEM");
                    res = true;
                }
            }
            return res;
        }
    }
}

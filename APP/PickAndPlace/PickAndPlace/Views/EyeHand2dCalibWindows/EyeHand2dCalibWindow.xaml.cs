using Emgu.CV;
using Emgu.CV.Structure;
using PickAndPlace.Controller.Robot;
using PickAndPlace.Controllers;
using PickAndPlace.Controllers.APIs;
using PickAndPlace.Controllers.Camera;
using PickAndPlace.Models;
using PickAndPlace.Utils;
using PickAndPlace.Views.UtilitiesWindows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PickAndPlace.Views.EyeHand2dCalibWindows
{
    /// <summary>
    /// Interaction logic for EyeHand2dCalibWindow.xaml
    /// </summary>
    public partial class EyeHand2dCalibWindow : Window, INotifyPropertyChanged
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        Properties.Settings _param = Properties.Settings.Default;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ObservableCollection<PairPoint> PairPoints { get; set; } = new ObservableCollection<PairPoint>();
        public bool CameraConnected { get; set; } = false;
        public bool CanSelectPoint { get; set; } = false;
        public bool CalibFinished { get; set; } = false;
        public bool CanValidate { get; set; } = false;
        public bool CanRemove => PairPoints.Count > 0;
        public bool CanCalib=> PairPoints.Count >= 4;
        private PairPoint _selectedPairPoint;

        public PairPoint SelectedPairPoint
        {
            get => _selectedPairPoint;
            set
            {
                if (_selectedPairPoint != value)
                {
                    _selectedPairPoint = value;
                    OnPropertyChanged();
                }
            }
        }

   

        CameraManager _cameraManager;
        LincolnCamera _cam;
        private bool _isSelecting;
        private bool _isValidating;
        private Image<Bgr, byte> _curImage;
        private DobotRobotClient _robot;

        public EyeHand2dCalibWindow()
        {
            InitializeComponent();
            Init();
            PairPoints.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(CanRemove));
                OnPropertyChanged(nameof(CanCalib));
            };
            DataContext = this;
        }
        private void Init()
        {
            _cameraManager = CameraManager.GetInstance();

            List<CamInfo> camInfoList = LincolnCamera.GetListCamInfo();
            for (int i = 0; i < camInfoList.Count; i++)
            {
                cbbCamSn.Items.Add(camInfoList[i].SN);
            }
        }

        private async void btnConnectCamera_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (cbbCamSn.SelectedItem == null)
            {
                var error = new ErrorWindow("Please choose a camera!\rHãy chọn camera!");
                error.ShowDialog();
                return;
            }

            string camSn = cbbCamSn.SelectedValue.ToString();

            //string camSn = "aaaa";
            try
            {
                await Task.Run(() =>
                {
                    //// Debug
                    //CameraConnected = true;
                    //OnPropertyChanged(nameof(CameraConnected));
                    if (_cam != null && _cam.IsOpen())
                        return;

                    _cam = _cameraManager.GetCamera(camSn) as LincolnCamera;
               
                    if (!_cam.IsOpen())
                        throw new Exception($"Cannot open camera {camSn}!");

                    _cam.SetWorkMode(CameraWorkMode.SoftwareTrigger);
                    _cam.Start();
                    CameraConnected = true;
                    OnPropertyChanged(nameof(CameraConnected));
                });
            }
            catch (Exception ex)
            {
                var error = new ErrorWindow($"{ex.Message}\rKhông mở được camera {camSn}!");
                error.ShowDialog();
            }
        }
        private void UpdateImage(System.Drawing.Bitmap image)
        {
            if (image == null)
            {
                imbImage.Source = null;
            }
            else if (imbImage.Source == null)
            {
                imbImage.SourceFromBitmap = image;
                var scale = GetFittedZoomScale(imbImage, image.Width, image.Height);
                imbImage.SetZoomScale(scale);
            }
            else
            {
                imbImage.SourceFromBitmap = image;
            }
        }
        private double GetFittedZoomScale(object imb, double imageWidth, double imageHeight)
        {
            var imageBox = imb as Heal.MyControl.ImageBox;
            var imageBoxWidth = imageBox.ActualWidth;
            var imageBoxHeight = imageBox.ActualHeight;
            var scale = Math.Min(imageBoxWidth / imageWidth, imageBoxHeight / imageHeight);
            return scale;
        }
        private void UpdatePixelCoord(double x, double y)
        {
            this.Dispatcher.Invoke(() =>
            { 
                tbImageX.Text = x.ToString("0.00");
                tbImageY.Text = y.ToString("0.00");
            });

        }
        private void UpdateRobotCoord(double x, double y)
        {
            this.Dispatcher.Invoke(() =>
            {
                tbRobotX.Text = x.ToString("0.00");
                tbRobotY.Text = y.ToString("0.00");
            });

        }
        private void UpdateValidationPixelCoord(double x, double y)
        {
            this.Dispatcher.Invoke(() =>
            {
                tbValidImageX.Text = x.ToString("0.00");
                tbValidImageY.Text = y.ToString("0.00");
            });

        }
        private void UpdateValidationRobotCoord(double x, double y)
        {
            this.Dispatcher.Invoke(() =>
            {
                tbValidRobotX.Text = x.ToString("0.00");
                tbValidRobotY.Text = y.ToString("0.00");
            });

        }
        private void btnCaptureImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var bitmap = _cam.TriggerAndGetFrame();
            //System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(@"D:\huynhvc\OTHERS\pick_and_place\APP\Image_20260307110740938.bmp");
            _curImage = new Image<Bgr, byte>(bitmap);
            UpdateImage(bitmap);
            CanSelectPoint = true;
            OnPropertyChanged(nameof(CanSelectPoint));
        }

        private void btnLoadIntrinsicCalib_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void btnSelectPoint_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_curImage == null) 
            { 
                var error = new ErrorWindow("Please capture an image!\rHãy chụp 1 ảnh camera!");
                error.ShowDialog();
                return;
            }
        
            btnSelectValidPoint.StartColor = System.Windows.Media.Color.FromRgb(0xC8, 0xAA, 0xAA);
            btnSelectValidPoint.EndColor = System.Windows.Media.Color.FromRgb(0x1C, 0x4D, 0x8D);
            if (_isValidating)
            {
                _isValidating = false;
                UpdateImage(_curImage.Bitmap);
            }
            if (!_isSelecting)
            {
                imbImage.Cursor = Cursors.Cross;
                Cursor = Cursors.Cross;
                btnSelectPoint.StartColor = Colors.DarkGreen;
                btnSelectPoint.EndColor = Colors.DarkGreen;
                _isSelecting = true;
            }
            else
            {
                imbImage.Cursor = Cursors.Arrow;
                Cursor = Cursors.Arrow;
                btnSelectPoint.StartColor = System.Windows.Media.Color.FromRgb(0xC8, 0xAA, 0xAA);
                btnSelectPoint.EndColor =  System.Windows.Media.Color.FromRgb(0x1C, 0x4D, 0x8D);
                _isSelecting = false;
            }
        }

        private async void btnGetRobotCoord_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckAndStartRobotAsync())
            {
                var error = new ErrorWindow("Robot is not connected!");
                error.ShowDialog();
                return;
            }

            try
            {
                string response = await _robot.GetPoseAsync();

                if (!TryParseDobotPose(response, out double robotX, out double robotY))
                {
                    var error = new ErrorWindow($"Cannot parse robot pose!\rResponse: {response}");
                    error.ShowDialog();
                    return;
                }

                UpdateRobotCoord(robotX, robotY);
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex.Message, "SYSTEM");

                var error = new ErrorWindow($"Cannot get robot pose, err: {ex.Message}");
                error.ShowDialog();
            }
        }
        private async Task<bool> CheckAndStartRobotAsync()
        {
            try
            {
                if (_robot != null && _robot.IsConnected())
                    return true;

                _robot?.Dispose();

                // Dobot mặc định dùng IP 192.168.5.1, port 8000.
                // Nếu _param.RobotIp đã config đúng thì dùng setting này.
                _robot = new DobotRobotClient(
                    ipAddress: _param.RobotIp,
                    port: 8000,
                    timeoutMs: _param.ReadPoseTimeout
                );

                bool connected = await _robot.ConnectAsync();

                if (!connected)
                {
                    _robot.Dispose();
                    _robot = null;
                    return false;
                }

                return _robot.IsConnected();
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex.Message, "SYSTEM");

                _robot?.Dispose();
                _robot = null;

                return false;
            }
        }

        private void btnResetPoint_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                tbImageX.Text = "";
                tbImageY.Text = "";
                tbRobotX.Text = "";
                tbRobotY.Text = "";
                if (_curImage == null)
                    return;
                UpdateImage(_curImage.Bitmap);
            });
        }

        private void btnAddPoint_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Check varlid pair point
            if (tbImageX.Text == "" || tbImageY.Text == "" || tbRobotX.Text == "" || tbRobotY.Text == "")
            { 
                var error = new ErrorWindow("Please select/get a pair of pixel point and robot coordinate!\rHãy chọn 1 cặp pixel point với 1 robot coordinate!");
                error.ShowDialog();
                return;
            }

            // Check if ImagePixel or RobotCoord point exists in PairPoints
            foreach (PairPoint point in PairPoints)
            {
                if (point.ImagePixel.Item1 == double.Parse(tbImageX.Text) && point.ImagePixel.Item2 == double.Parse(tbImageY.Text))
                {
                    var error = new ErrorWindow("ImagePixel point already exists!\rĐiểm ảnh pixel đã tồn tại!");
                    error.ShowDialog();
                    return;
                }
                if (point.RobotCoord.Item1 == double.Parse(tbRobotX.Text) && point.RobotCoord.Item2 == double.Parse(tbRobotY.Text))
                {
                    var error = new ErrorWindow("RobotCoord point already exists!\rTọa độ robot đã tồn tại!");
                    error.ShowDialog();
                    return;
                }
            }

            // Add point
            Tuple<double, double> imagePixel = new Tuple<double, double>(double.Parse(tbImageX.Text), double.Parse(tbImageY.Text));
            Tuple<double, double> robotCoord = new Tuple<double, double>(double.Parse(tbRobotX.Text), double.Parse(tbRobotY.Text));
            PairPoint pairPoint = new PairPoint(PairPoints.Count + 1, imagePixel, robotCoord);

            PairPoints.Add(pairPoint);
        }

        private void btnRemoveRow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var warning = new WarningWindow("Are you sure to remove this pair point?\rBạn có muốn xóa cặp điểm này?");
            var res = warning.ShowDialog();
            if (!(bool)res)
                return;
            PairPoints.Remove(SelectedPairPoint);
            ReUpdateIndex();
        }
        private void ReUpdateIndex()
        {
            for (var i = 0; i < PairPoints.Count; i++)
            {
                PairPoints[i].Id = i + 1;
            }
        }

        private void btnClearAll_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var warning = new WarningWindow("Are you sure to clear this table data?\rBạn có muốn xóa dữ liệu bảng này?");
            var res = warning.ShowDialog();
            if (!(bool)res)
                return;
            PairPoints.Clear();
        }

        private void btnCalibrate_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Check if PairPoints >= 4
            if (4 <= PairPoints.Count && PairPoints.Count < 9)
            {
                var waining = new WarningWindow("Suggested to have at least 9 pair points, still process?\rKết quả tốt hơn với it nhất 9 cặp điểm, tiếp tục tiến hành?");
                bool? answer = waining.ShowDialog();
                if (!(bool)answer)
                    return;
            }
            var res = APICommunication.Calibration2D(_param.ApiUrlAi, PairPoints);
            if (res!=null && res.Result)
            {
                var info = new InformationWindow("Calibration successfully!\rCalibration thành công!");
                info.ShowDialog();
                CalibFinished = true;
                CanValidate = true;
                OnPropertyChanged(nameof(CalibFinished));
                OnPropertyChanged(nameof(CanValidate));
            }
            else
            {
                var error = new ErrorWindow("Calibration failed, check your data!\rCalibration thất bại, kiểm tra lại dữ liệu!");
                error.ShowDialog();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void imbImage_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (imbImage == null)
                return;
            if (e.ClickCount == 2)
            {
                ResetView();
            }   

            if (!_isSelecting && !_isValidating)
                return;

            var control = sender as Heal.MyControl.ImageBox;
            System.Windows.Point p = Mouse.GetPosition(control);
            double scale = control.ZoomScale;
            double x = control.TranslateX;
            double y = control.TranslateY;
            var clickPosition = new System.Windows.Point(p.X / scale - x / scale, p.Y / scale - y / scale);

            Console.WriteLine("X: " + clickPosition.X + " Y: " + clickPosition.Y);
            if (_isSelecting)
            {
                UpdatePixelCoord(clickPosition.X, clickPosition.Y);
                using (var image = _curImage.Clone())
                {
                    CvInvoke.Circle(image, new System.Drawing.Point((int)clickPosition.X, (int)clickPosition.Y), 2, new MCvScalar(0, 0, 255), -1);
                    UpdateImage(image.Bitmap);
                }
            }
            else if (_isValidating)
            {
                UpdateValidationPixelCoord(clickPosition.X, clickPosition.Y);
                using (var image = _curImage.Clone())
                {
                    CvInvoke.Circle(image, new System.Drawing.Point((int)clickPosition.X, (int)clickPosition.Y), 2, new MCvScalar(0, 255, 0), -1);
                    UpdateImage(image.Bitmap);
                }

                var res = APICommunication.TransformPoint(_param.ApiUrlAi, clickPosition.X, clickPosition.Y);
                UpdateValidationRobotCoord((double)res.RobotX, (double)res.RobotY);

            }
        }

        private void ResetView()
        {
            if (!object.ReferenceEquals(imbImage.Source, null))
            {
                var scaleMap = GetFittedZoomScale(imbImage, imbImage.Source.Width, imbImage.Source.Height);
                imbImage.SetZoomScale(scaleMap);
                imbImage.GoToXY(0, 0);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_cam != null && _cam.IsOpen())
            {
                _cam.Stop();
                _logger.Debug("Camera stopped!");
            }
            if (_robot != null)
            {
                _robot.Dispose();
                _logger.Debug("Robot disposed!");
            }
        }

        private void btnSelectValidPoint_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_curImage == null)
            {
                var error = new ErrorWindow("Please capture an image!\rHãy chụp 1 ảnh camera!");
                error.ShowDialog();
                return;
            }
       
            btnSelectPoint.StartColor = System.Windows.Media.Color.FromRgb(0xC8, 0xAA, 0xAA);
            btnSelectPoint.EndColor = System.Windows.Media.Color.FromRgb(0x1C, 0x4D, 0x8D);
            UpdateImage(_curImage.Bitmap);
            if (_isSelecting)
            {
                _isSelecting = false;
                UpdateImage(_curImage.Bitmap);
            }
            if (!_isValidating)
            {
                imbImage.Cursor = Cursors.Cross;
                Cursor = Cursors.Cross;
                btnSelectValidPoint.StartColor = Colors.DarkGreen;
                btnSelectValidPoint.EndColor = Colors.DarkGreen;
                _isValidating = true;
            }
            else
            {
                imbImage.Cursor = Cursors.Arrow;
                Cursor = Cursors.Arrow;
                btnSelectValidPoint.StartColor = System.Windows.Media.Color.FromRgb(0xC8, 0xAA, 0xAA);
                btnSelectValidPoint.EndColor = System.Windows.Media.Color.FromRgb(0x1C, 0x4D, 0x8D);
                _isValidating = false;
            }
        }

        private async void btnMoveRobotCoord_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_curImage == null)
            {
                var error = new ErrorWindow("Image is not captured!\rHãy chụp 1 ảnh camera!");
                error.ShowDialog();
                return;
            }

            if (!TryParseDouble(tbValidRobotX.Text, out double robotX) ||
                !TryParseDouble(tbValidRobotY.Text, out double robotY))
            {
                var error = new ErrorWindow("Robot coordinate is invalid!\rTọa độ robot không hợp lệ!");
                error.ShowDialog();
                return;
            }

            if (!await CheckAndStartRobotAsync())
            {
                var error = new ErrorWindow("Robot is not connected!");
                error.ShowDialog();
                return;
            }

            try
            {
                bool moved = await _robot.MoveAsync(robotX, robotY);

                if (!moved)
                {
                    var error = new ErrorWindow("Move robot failed!\rDi chuyển robot thất bại!");
                    error.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex.Message, "SYSTEM");

                var error = new ErrorWindow($"Move robot failed, err: {ex.Message}");
                error.ShowDialog();
            }
        }

        private void btnSaveMatrix_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var res = APICommunication.SaveMatrix(_param.ApiUrlAi);
            if (res != null && res.Result)
            {
                var info = new InformationWindow("Save calibration result successfully!\rLưu kết quả calibration thành công!");
                info.ShowDialog();
            }
            else
            {
                var error = new ErrorWindow("Save calibration result failed!\rLưu kết quả calibration thất bại!");
                error.ShowDialog();
            }
        }

        private void btnLoadExistingMatrix_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var res = APICommunication.LoadExistingMatrix(_param.ApiUrlAi);
            if (res!=null && res.Result)
            {
                var info = new InformationWindow("Load existing calibration result successfully!\rLoad kết quả calibration cũ thành công!");
                info.ShowDialog();
                CanValidate = true;
                OnPropertyChanged(nameof(CanValidate));
            }
            else
            {
                var error = new ErrorWindow("Not found existing calibration result!\rKhông tìm thấy kết quả calibration cũ!");
                error.ShowDialog();
            }
        }
        private static bool TryParseDouble(string text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseDobotPose(string response, out double x, out double y)
        {
            x = 0;
            y = 0;

            if (string.IsNullOrWhiteSpace(response))
                return false;

            string text = response.Trim();

            // Support format kiểu Dobot API:
            // 0,{100.1,200.2,50,0,0,90},GetPose();
            int leftBrace = text.IndexOf('{');
            int rightBrace = leftBrace >= 0 ? text.IndexOf('}', leftBrace + 1) : -1;

            if (leftBrace >= 0 && rightBrace > leftBrace)
            {
                text = text.Substring(leftBrace + 1, rightBrace - leftBrace - 1);
            }

            // Support format custom:
            // POSE,100.1,200.2,50,0,0,90
            // hoặc:
            // POSE 100.1 200.2 50 0 0 90
            var matches = Regex.Matches(text, @"[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?");

            if (matches.Count < 2)
                return false;

            bool okX = double.TryParse(
                matches[0].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out x
            );

            bool okY = double.TryParse(
                matches[1].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out y
            );

            return okX && okY;
        }
        //private void GetDisplayedImageInfo(out double displayedW,
        //                           out double displayedH,
        //                           out double offsetX,
        //                           out double offsetY)
        //{
        //    double imgW = imbImage.Source.Width;
        //    double imgH = imbImage.Source.Height;

        //    double controlW = imbImage.ActualWidth;
        //    double controlH = imbImage.ActualHeight;

        //    double ratio = Math.Min(controlW / imgW, controlH / imgH);

        //    displayedW = imgW * ratio;
        //    displayedH = imgH * ratio;

        //    offsetX = (controlW - displayedW) / 2.0;
        //    offsetY = (controlH - displayedH) / 2.0;
        //}

        //private void imbImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        //{
        //    if (!_isSelecting)
        //    {
        //        imbImage.CaptureMouse();
        //        _start = e.GetPosition(this);
        //        _origin = new System.Windows.Point(translateTransform.X, translateTransform.Y);
        //        Cursor = Cursors.Hand;
        //    }

        //    else
        //    {
        //        if (imbImage.Source == null) return;

        //        Point mouse = e.GetPosition(overlayCanvas);

        //        GetDisplayedImageInfo(out double displayedW,
        //                              out double displayedH,
        //                              out double offsetX,
        //                              out double offsetY);

        //        double x = mouse.X - offsetX;
        //        double y = mouse.Y - offsetY;

        //        if (x < 0 || y < 0 || x > displayedW || y > displayedH)
        //            return;

        //        double pixelX = x * imbImage.Source.Width / displayedW;
        //        double pixelY = y * imbImage.Source.Height / displayedH;

        //        Console.WriteLine($"({pixelX}, {pixelY})");

        //        HighlightPoint(new Point(pixelX, pixelY));
        //    }
        //}

        //private void HighlightPoint(System.Windows.Point pixelPoint)
        //{
        //    overlayCanvas.Children.Clear();

        //    GetDisplayedImageInfo(out double displayedW,
        //                          out double displayedH,
        //                          out double offsetX,
        //                          out double offsetY);

        //    double uiX = pixelPoint.X * displayedW / imbImage.Source.Width + offsetX;
        //    double uiY = pixelPoint.Y * displayedH / imbImage.Source.Height + offsetY;

        //    Ellipse dot = new Ellipse()
        //    {
        //        Width = 10,
        //        Height = 10,
        //        Fill = Brushes.Red
        //    };

        //    Canvas.SetLeft(dot, uiX - 5);
        //    Canvas.SetTop(dot, uiY - 5);

        //    overlayCanvas.Children.Add(dot);
        //}

        //private void imbImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        //{
        //    if (!_isSelecting)
        //    {
        //        imbImage.ReleaseMouseCapture();
        //        Cursor = Cursors.Arrow;
        //    }

        //}

        //private void imbImage_MouseMove(object sender, MouseEventArgs e)
        //{
        //    if (!imbImage.IsMouseCaptured) return;

        //    System.Windows.Point p = e.GetPosition(this);
        //    translateTransform.X = _origin.X + (p.X - _start.X);
        //    translateTransform.Y = _origin.Y + (p.Y - _start.Y);
        //}

        //private void imbImage_MouseDown(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.ClickCount == 2)
        //        ResetView();
        //}
        //private void imbImage_MouseWheel(object sender, MouseWheelEventArgs e)
        //{
        //    double zoom = e.Delta > 0 ? 1.1 : 0.9;

        //    scaleTransform.ScaleX *= zoom;
        //    scaleTransform.ScaleY *= zoom;
        //}
        //private async void ResetView()
        //{
        //    var duration = TimeSpan.FromMilliseconds(150);

        //    scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration));
        //    scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration));
        //    translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, duration));
        //    translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration));

        //    await Task.Delay(duration);

        //    // Xóa animation, trả quyền điều khiển về code
        //    scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        //    scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        //    translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
        //    translateTransform.BeginAnimation(TranslateTransform.YProperty, null);

        //    scaleTransform.ScaleX = 1;
        //    scaleTransform.ScaleY = 1;
        //    translateTransform.X = 0;
        //    translateTransform.Y = 0;
        //}
    }
}

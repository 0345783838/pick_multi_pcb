using Emgu.CV.Structure;
using Emgu.CV;
using PickAndPlace.Controllers.Camera;
using PickAndPlace.Models;
using PickAndPlace.Views.UtilitiesWindows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Drawing;
using System.Text.RegularExpressions;
using Emgu.CV.CvEnum;
using Point = System.Drawing.Point;
using Emgu.CV.Flann;
using PickAndPlace.Utils;
using System.Windows.Media;

namespace PickAndPlace.Views.ModelsManagerWindows
{
    /// <summary>
    /// Interaction logic for TemplatesSettingWindow.xaml
    /// </summary>
    public partial class TemplatesSettingWindow : Window, INotifyPropertyChanged
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        Properties.Settings _param = Properties.Settings.Default;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private ModelInfo _model;
        ModelsManagerWindow _modelsWindow;

        public ObservableCollection<Template> TemplatesList { get; set; } = new ObservableCollection<Template>();
        private Template _selectedTemplate;

        public Template SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (_selectedTemplate != value)
                {
                    _selectedTemplate = value;
                    OnPropertyChanged();
                    OnTemplateSelectionChanged();
                }
            }
        }

        private void OnTemplateSelectionChanged()
        {
            if (SelectedTemplate == null)
            {
                imbTemplate.Source = null; 
            }
            else
            {
                imbTemplate.Source = Converter.BitmapToBitmapSource(SelectedTemplate.Image.Bitmap);
            }
        }

        CameraManager _cameraManager;
        LincolnCamera _cam;
        private Image<Bgr, byte> _curImage;
        private Image<Bgr, byte> _showingImage;
        private bool _addingRoi = false;

        private Rectangle _curDrawingRoi;

        public Rectangle CurDrawingRoi
        {
            get => _curDrawingRoi;
            set
            {
                if (_curDrawingRoi != value)
                {
                    _curDrawingRoi = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanAddRemove));
                }
            }
        }

        private Rectangle _curHandle;
        private bool _isSelectingRoi = false;
        private bool _inRectPos;
        private bool _inHandlePos;
        private bool _isResizing;
        private bool _isMoving;
        private System.Windows.Point _clickPosition;
        private Rectangle _tempOriginRects;
        private Image<Bgr, byte> _curTempImage;

        public bool CameraConnected { get; set; } = false;
        public bool CanSave { get; set; } = false;
        public bool CanAddRemove => CurDrawingRoi != Rectangle.Empty;
        public bool CanProcesImage { get; set; } = false;
        public TemplatesSettingWindow(ModelsManagerWindow window, ModelInfo model)
        {
            InitializeComponent();
            _model = model;
            _modelsWindow = window;
            DataContext = this;
            Init();
        }
        private void Init()
        {
            _cameraManager = CameraManager.GetInstance();

            List<CamInfo> camInfoList = LincolnCamera.GetListCamInfo();
            for (int i = 0; i < camInfoList.Count; i++)
            {
                cbbCamSn.Items.Add(camInfoList[i].SN);
            }
            
            for (int i = 0; i < _model.Templates.Count; i++)
            {
                TemplatesList.Add(_model.Templates[i]);
            }
        }

        private void btnCaptureImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var bitmap = _cam.TriggerAndGetFrame();
            //System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(@"D:\huynhvc\OTHERS\pick_and_place\program\pick_and_place\Image_20260306205408882.bmp");
            _curImage = new Image<Bgr, byte>(bitmap);
            _showingImage = _curImage.Copy();
            UpdateCameraImage(bitmap);
            CanProcesImage = true;
            OnPropertyChanged(nameof(CanProcesImage));
        }

        private void UpdateCameraImage(Bitmap image)
        {
            if (image == null)
            {
                imbCameraImage.Source = null;
            }
            else if (imbCameraImage.Source == null)
            {
                imbCameraImage.SourceFromBitmap = image;
                var scale = GetFittedZoomScale(imbCameraImage, image.Width, image.Height);
                imbCameraImage.SetZoomScale(scale);
            }
            else
            {
                imbCameraImage.SourceFromBitmap = image;
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

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_cam != null && _cam.IsOpen())
            {
                _cam.Stop();
                _logger.Debug("Camera closed!");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void btnAddTemplate_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var template = new Models.Template(TemplatesList.Count, _curTempImage, $"{_param.MODELS_PATH}/{_model.Name}/{timestamp}.png");
            TemplatesList.Add(template);
            CurDrawingRoi = Rectangle.Empty;
            UpdateDrawingDetails();
            CanSave = true;
            OnPropertyChanged(nameof(CanSave));
        }

        private void btnCancel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var warning = new WarningWindow("Are you sure to cancel processes?");
            var res = warning.ShowDialog();
            if (res == true)
            {
                this.Close();
            }
        }

        private void btnSave_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _model.Templates = TemplatesList.ToList();
            _modelsWindow.UpdateModel(_model);
            var info = new InformationWindow("Save successfully!");
            info.ShowDialog();
            this.Close();
        }

        private void btnAddRoi_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_addingRoi)
            {
                btnAddRoi.StartColor = System.Windows.Media.Color.FromRgb(0xCC, 0xFF, 0xAA);
                btnAddRoi.EndColor = System.Windows.Media.Color.FromRgb(0x1E, 0x5B, 0x53);
                imbCameraImage.ResetDrawRectMode();
                this.Cursor = Cursors.Arrow;
                _addingRoi = false;
            }
            else
            {
                btnAddRoi.StartColor = Colors.DarkOrange;
                btnAddRoi.EndColor = Colors.DarkOrange;
                imbCameraImage.SetDrawRectMode();
                this.Cursor = Cursors.Cross;
                _addingRoi = true;
            }
        }

        private void btnRemoveRoi_MouseDown(object sender, MouseButtonEventArgs e)
        {
            CurDrawingRoi = Rectangle.Empty;
            OnPropertyChanged(nameof(CurDrawingRoi));
            UpdateDrawingDetails();
        }

        private void udtbAngle_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9.-]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void udtbAngle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_curImage == null) return;
            // Check valid angle
            double value;
            try
            {
                value = Convert.ToDouble(udtbAngle.Text);
            }
            catch
            {
                return;
            }

            _showingImage = RotateImage(_curImage, value);
            UpdateCameraImage(_showingImage.Bitmap);
            UpdateDrawingDetails();
        }
        //public static Image<Bgr, byte> RotateImage(Image<Bgr, byte> src, double angle)
        //{
        //    int w = src.Width;
        //    int h = src.Height;

        //    PointF center = new PointF(w / 2f, h / 2f);

        //    // tạo matrix xoay
        //    Mat rotMat = new Mat();
        //    CvInvoke.GetRotationMatrix2D(center, angle, 1.0, rotMat);

        //    // ảnh output
        //    Image<Bgr, byte> dst = new Image<Bgr, byte>(w, h);

        //    CvInvoke.WarpAffine(
        //        src,
        //        dst,
        //        rotMat,
        //        new System.Drawing.Size(w, h),
        //        Inter.Linear,
        //        Warp.Default,
        //        BorderType.Constant,
        //        new MCvScalar(0, 0, 0)
        //    );

        //    return dst;
        //}
        public static Image<Bgr, byte> RotateImage(Image<Bgr, byte> src, double angle)
        {
            int w = src.Width;
            int h = src.Height;

            PointF center = new PointF(w / 2f, h / 2f);

            Mat rotMat = new Mat();
            CvInvoke.GetRotationMatrix2D(center, angle, 1.0, rotMat);

            double rad = angle * Math.PI / 180.0;
            double sin = Math.Abs(Math.Sin(rad));
            double cos = Math.Abs(Math.Cos(rad));

            int newW = (int)(h * sin + w * cos);
            int newH = (int)(h * cos + w * sin);

            // dịch ảnh vào giữa canvas mới
            var m = new Emgu.CV.Matrix<double>(2, 3);
            rotMat.CopyTo(m);

            m[0, 2] += (newW / 2.0) - center.X;
            m[1, 2] += (newH / 2.0) - center.Y;

            Image<Bgr, byte> dst = new Image<Bgr, byte>(newW, newH);

            CvInvoke.WarpAffine(
                src,
                dst,
                m,
                new System.Drawing.Size(newW, newH),
                Inter.Linear,
                Warp.Default,
                BorderType.Constant,
                new MCvScalar(0, 0, 0)
            );

            return dst;
        }

        private void imbCameraImage_MouseClick(object sender, EventArgs e)
        {
            if (_inHandlePos) return;
            if (_showingImage == null) return;

            var control = sender as Heal.MyControl.ImageBox;
            System.Windows.Point p = Mouse.GetPosition(control);
            double scale = control.ZoomScale;
            double x = control.TranslateX;
            double y = control.TranslateY;
            System.Windows.Point selectedPoint = new System.Windows.Point(p.X / scale - x / scale, p.Y / scale - y / scale);

            if (CurDrawingRoi.Contains((int)selectedPoint.X, (int)selectedPoint.Y))
            {
                _isSelectingRoi = true;
                SelectedTemplate = null;
                tbRoiX.Focus();
            }
            else
            {
                _isSelectingRoi = false;
            }
            UpdateDrawingDetails();
        }

        private void imbCameraImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelectingRoi) return;

            _inRectPos = false;
            _inHandlePos = false;
            var control = sender as Heal.MyControl.ImageBox;
            System.Windows.Point p = Mouse.GetPosition(control);
            double scale = control.ZoomScale;
            double x = control.TranslateX;
            double y = control.TranslateY;
            var clickPosition = new System.Windows.Point(p.X / scale - x / scale, p.Y / scale - y / scale);

           
            if (!_isMoving || !_isResizing)
            {
                // Check if mouse is in rect
                if (CurDrawingRoi.Contains((int)clickPosition.X, (int)clickPosition.Y))
                {
                    _inRectPos = true;
                }
                if (_inRectPos) imbCameraImage.SetDrawRectMode(); else imbCameraImage.ResetDrawRectMode();
                // Check mouse if is any handle
                if (_curHandle.Contains((int)clickPosition.X, (int)clickPosition.Y))
                {
                    _inHandlePos = true;
                }
            }

            // Case moving rect
            if (_isMoving)
            {
                imbCameraImage.SetDrawRectMode();
                int shiftX = Convert.ToInt32(clickPosition.X - _clickPosition.X);
                int shiftY = Convert.ToInt32(clickPosition.Y - _clickPosition.Y);

                if (_tempOriginRects == Rectangle.Empty) return;
                if (CurDrawingRoi == Rectangle.Empty) return;
                CurDrawingRoi = new Rectangle(_tempOriginRects.X + shiftX, _tempOriginRects.Y + shiftY, _tempOriginRects.Width, _tempOriginRects.Height);
                OnPropertyChanged(nameof(CurDrawingRoi));
              
                UpdateDrawingDetails();
            }

            // Case resizing rect
            if (_isResizing)
            {
                imbCameraImage.SetDrawRectMode();
                int shiftX = Convert.ToInt32(clickPosition.X - _clickPosition.X);
                int shiftY = Convert.ToInt32(clickPosition.Y - _clickPosition.Y);

                if (_tempOriginRects == Rectangle.Empty) return;
                if (CurDrawingRoi == Rectangle.Empty) return;

                var newWidth = _tempOriginRects.Width + shiftX;
                var newHeight = _tempOriginRects.Height + shiftY;

                CurDrawingRoi = new Rectangle(_tempOriginRects.X, _tempOriginRects.Y, newWidth, newHeight);
                OnPropertyChanged(nameof(CurDrawingRoi));
                UpdateDrawingDetails();
            }
        }

        private void imbCameraImage_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_inHandlePos && !_isResizing && !_addingRoi && !_isMoving)
            {
                imbCameraImage.SetDrawRectMode();
                _isResizing = true;
                var control = sender as Heal.MyControl.ImageBox;
                System.Windows.Point p = Mouse.GetPosition(control);
                double scale = control.ZoomScale;
                double x = control.TranslateX;
                double y = control.TranslateY;
                _clickPosition = new System.Windows.Point(p.X / scale - x / scale, p.Y / scale - y / scale);
                _tempOriginRects = new Rectangle(CurDrawingRoi.X, CurDrawingRoi.Y, CurDrawingRoi.Width, CurDrawingRoi.Height);
                return;
            }

            if (_inRectPos && !_isMoving && !_addingRoi && !_isResizing )
            {
                _isMoving = true;
                var control = sender as Heal.MyControl.ImageBox;
                System.Windows.Point p = Mouse.GetPosition(control);
                double scale = control.ZoomScale;
                double x = control.TranslateX;
                double y = control.TranslateY;
                _clickPosition = new System.Windows.Point(p.X / scale - x / scale, p.Y / scale - y / scale);
                _tempOriginRects = new Rectangle(CurDrawingRoi.X, CurDrawingRoi.Y, CurDrawingRoi.Width, CurDrawingRoi.Height);
                return;
            }
        }

        private void imbCameraImage_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_showingImage == null)
                return;

            if (_isMoving)
            {
                _isMoving = false;
                imbCameraImage.ResetDrawRectMode();
                _tempOriginRects = Rectangle.Empty;
                UpdateDrawingDetails();
            }
            if (_isResizing)
            {
                _isResizing = false;
                imbCameraImage.ResetDrawRectMode();
                _tempOriginRects = Rectangle.Empty;
                UpdateDrawingDetails();
            }

            if (_addingRoi)
            {
                var rect = imbCameraImage.GetSelectRectangle();
                if (rect == new Rect())
                    return;
                _addingRoi = false;
                CurDrawingRoi = new System.Drawing.Rectangle(Convert.ToInt32(rect.X), Convert.ToInt32(rect.Y), Convert.ToInt32(rect.Width), Convert.ToInt32(rect.Height));
                OnPropertyChanged(nameof(CurDrawingRoi));

                this.Cursor = Cursors.Arrow;
                this.imbCameraImage.ResetDrawRectMode();
                btnAddRoi.StartColor = System.Windows.Media.Color.FromRgb(0xCC, 0xFF, 0xAA);
                btnAddRoi.EndColor = System.Windows.Media.Color.FromRgb(0x1E, 0x5B, 0x53);
            }
            UpdateDrawingDetails();
        }
        private Rectangle GetHandle(Rectangle drawingRoi, int handleSize)
        {
            var bottomRight = new Point(drawingRoi.X + drawingRoi.Width, drawingRoi.Y + drawingRoi.Height);
            Rectangle handle = new Rectangle(bottomRight.X - handleSize / 2, bottomRight.Y - handleSize / 2, handleSize, handleSize);
         
            return handle;
        }
        private void UpdateDrawingDetails()
        {
            if (_showingImage == null)
                return;
            _curHandle = GetHandle(CurDrawingRoi, 10);
            using (Image<Bgr, byte> image = _showingImage.Copy())
            {
                if (_isSelectingRoi)
                {
                    if (_isMoving)
                    {
                        CvInvoke.Rectangle(image, CurDrawingRoi, new MCvScalar(0, 127, 255), 1);
                    }
                    else
                    {
                        CvInvoke.Rectangle(image, CurDrawingRoi, new MCvScalar(0, 0, 255), 1);
                    }

                    if (_isResizing)
                    {
                        CvInvoke.Rectangle(image, _curHandle, new MCvScalar(0, 127, 255), -1);
                    }
                    else
                    {
                        CvInvoke.Rectangle(image, _curHandle, new MCvScalar(0, 0, 255), -1);
                    }
                }
                else
                {
                    CvInvoke.Rectangle(image, CurDrawingRoi, new MCvScalar(0, 255, 0), 1);
                }

                UpdateCameraImage(image.Bitmap);
                if (CurDrawingRoi == Rectangle.Empty)
                {
                    _curTempImage = null;
                    imbTemplate.Source = null;
                }
                else
                {
                    _curTempImage = _showingImage.Copy(CurDrawingRoi);
                    imbTemplate.Source = Converter.BitmapToBitmapSource(_curTempImage.Bitmap);
                }
            }
        }

        private void dgTemplatesList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                if (TemplatesList.Count == 0) return;

                var warining = new WarningWindow($"Are you sure to delete Template {SelectedTemplate.Id}?");
                var res = warining.ShowDialog();

                if (res != true) return;


                TemplatesList.Remove(SelectedTemplate);
                SelectedTemplate = null;

                // Re-update index
                for (int i = 0; i < TemplatesList.Count; i++)
                {
                    TemplatesList[i].Id = i;
                }
                CanSave = true;
                OnPropertyChanged(nameof(CanSave));
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isSelectingRoi) return;


            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Right)
            {
                if (CurDrawingRoi.X + CurDrawingRoi.Width == _showingImage.Width) return;
                CurDrawingRoi = new Rectangle(CurDrawingRoi.X, CurDrawingRoi.Y, CurDrawingRoi.Width + 1, CurDrawingRoi.Height);
                OnPropertyChanged(nameof(CurDrawingRoi));
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Left)
            {
                if (CurDrawingRoi.Width <= 1) return;
                CurDrawingRoi = new Rectangle(CurDrawingRoi.X, CurDrawingRoi.Y, CurDrawingRoi.Width - 1, CurDrawingRoi.Height);
                OnPropertyChanged(nameof(CurDrawingRoi));
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
            {
                if (CurDrawingRoi.Y + CurDrawingRoi.Height == _showingImage.Height) return;
                CurDrawingRoi = new Rectangle(CurDrawingRoi.X, CurDrawingRoi.Y, CurDrawingRoi.Width, CurDrawingRoi.Height + 1);
                OnPropertyChanged(nameof(CurDrawingRoi));
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
            {
                if (CurDrawingRoi.Height <= 1) return;
                CurDrawingRoi = new Rectangle(CurDrawingRoi.X, CurDrawingRoi.Y, CurDrawingRoi.Width, CurDrawingRoi.Height - 1);
                OnPropertyChanged(nameof(CurDrawingRoi));
            }
            else if (e.Key == Key.Left)
            {
                if (CurDrawingRoi.X == 0) return;
                CurDrawingRoi = new Rectangle(CurDrawingRoi.X - 1, CurDrawingRoi.Y, CurDrawingRoi.Width, CurDrawingRoi.Height);
                OnPropertyChanged(nameof(CurDrawingRoi));
            }
            else if (e.Key == Key.Right)
            {
                if (CurDrawingRoi.X + CurDrawingRoi.Width == _showingImage.Width) return;
                CurDrawingRoi = new Rectangle(CurDrawingRoi.X + 1, CurDrawingRoi.Y, CurDrawingRoi.Width, CurDrawingRoi.Height);
                OnPropertyChanged(nameof(CurDrawingRoi));
            }
            else if (e.Key == Key.Up)
            {
                if (CurDrawingRoi.Y == 0) return;
                CurDrawingRoi = new Rectangle(CurDrawingRoi.X, CurDrawingRoi.Y - 1, CurDrawingRoi.Width, CurDrawingRoi.Height);
                OnPropertyChanged(nameof(CurDrawingRoi));
            }
            else if (e.Key == Key.Down)
            {
                if (CurDrawingRoi.Y + CurDrawingRoi.Height == _showingImage.Height) return;
                CurDrawingRoi = new Rectangle(CurDrawingRoi.X, CurDrawingRoi.Y + 1, CurDrawingRoi.Width, CurDrawingRoi.Height);
                OnPropertyChanged(nameof(CurDrawingRoi));
            }
            UpdateDrawingDetails();
        }
    }
}

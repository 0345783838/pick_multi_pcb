using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using LincolnVision.ImageViewer;
using PickAndPlace.Controllers.Camera;
using PickAndPlace.Models;
using PickAndPlace.Utils;
using PickAndPlace.Views.UtilitiesWindows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace PickAndPlace.Views.ModelsManagerWindows
{
    /// <summary>
    /// Interaction logic for TemplatesSettingWindow.xaml
    /// Integrated with LincolnVision.ImageViewer DLL.
    /// C# 7.3 compatible code-behind.
    /// </summary>
    public partial class TemplatesSettingWindow : Window, INotifyPropertyChanged
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        Properties.Settings _param = Properties.Settings.Default;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly ModelInfo _model;
        private readonly ModelsManagerWindow _modelsWindow;

        public ObservableCollection<Template> TemplatesList { get; set; } = new ObservableCollection<Template>();

        private Template _selectedTemplate;
        public Template SelectedTemplate
        {
            get { return _selectedTemplate; }
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

        private CameraManager _cameraManager;
        private LincolnCamera _cam;
        private Image<Bgr, byte> _curImage;
        private Image<Bgr, byte> _curTempImage;

        private bool _addingRoi;
        private bool _updatingAngleFromViewer;

        private double _roiCx;
        public double RoiCx
        {
            get { return _roiCx; }
            set
            {
                if (Math.Abs(_roiCx - value) > 0.0001)
                {
                    _roiCx = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _roiCy;
        public double RoiCy
        {
            get { return _roiCy; }
            set
            {
                if (Math.Abs(_roiCy - value) > 0.0001)
                {
                    _roiCy = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _roiWidth;
        public double RoiWidth
        {
            get { return _roiWidth; }
            set
            {
                if (Math.Abs(_roiWidth - value) > 0.0001)
                {
                    _roiWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _roiHeight;
        public double RoiHeight
        {
            get { return _roiHeight; }
            set
            {
                if (Math.Abs(_roiHeight - value) > 0.0001)
                {
                    _roiHeight = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _roiAngleDeg;
        public double RoiAngleDeg
        {
            get { return _roiAngleDeg; }
            set
            {
                if (Math.Abs(_roiAngleDeg - value) > 0.0001)
                {
                    _roiAngleDeg = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CameraConnected { get; set; }
        public bool CanSave { get; set; }
        public bool CanProcesImage { get; set; }

        public bool CanAddRemove
        {
            get { return GetCurrentRotatedRectangle() != null; }
        }

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

            InitImageViewer();
            ResetRoiInfo();
        }

        private void InitImageViewer()
        {
            imageViewer.CurrentTool = DrawingTool.Select;
            imageViewer.ShowCrosshair = true;
            imageViewer.ShowImageBorder = true;
            imageViewer.ResetViewOnDoubleClick = true;
            imageViewer.EnableMiddleButtonPan = true;
            imageViewer.EnableRightButtonPan = true;

            imageViewer.ShapeCreated += ImageViewer_ShapeCreated;
            imageViewer.ShapeChanged += ImageViewer_ShapeChanged;
            imageViewer.ShapeSelectionChanged += ImageViewer_ShapeSelectionChanged;
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

        private async void btnConnectCamera_MouseDown(object sender, MouseButtonEventArgs e)
        {
            CameraConnected = true;
            OnPropertyChanged(nameof(CameraConnected));
            return;

            if (cbbCamSn.SelectedItem == null)
            {
                var error = new ErrorWindow("Please choose a camera!\rHãy chọn camera!");
                error.ShowDialog();
                return;
            }

            string camSn = cbbCamSn.SelectedValue.ToString();

            try
            {
                await Task.Run(() =>
                {
                    if (_cam != null && _cam.IsOpen())
                        return;

                    _cam = _cameraManager.GetCamera(camSn) as LincolnCamera;

                    if (_cam == null || !_cam.IsOpen())
                        throw new Exception(string.Format("Cannot open camera {0}!", camSn));

                    _cam.SetWorkMode(CameraWorkMode.SoftwareTrigger);
                    _cam.Start();

                    CameraConnected = true;
                    OnPropertyChanged(nameof(CameraConnected));
                });
            }
            catch (Exception ex)
            {
                var error = new ErrorWindow(string.Format("{0}\rKhông mở được camera {1}!", ex.Message, camSn));
                error.ShowDialog();
            }
        }

        private void btnCaptureImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            //Bitmap bitmap = _cam.TriggerAndGetFrame();
            // Debug sample:
            Bitmap bitmap = new Bitmap(@"D:\huynhvc\OTHERS\pick_and_place\program\pick_and_place\Image_20260306205408882.bmp");

            _curImage = new Image<Bgr, byte>(bitmap);
            _curTempImage = null;
            imbTemplate.Source = null;

            imageViewer.ClearShapes();
            BitmapSource source = Converter.BitmapToBitmapSource(bitmap);
            imageViewer.LoadImage(source);
            imageViewer.Focus();

            CanProcesImage = true;
            OnPropertyChanged(nameof(CanProcesImage));
            OnPropertyChanged(nameof(CanAddRemove));

            ResetRoiInfo();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            imageViewer.Focus();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_cam != null && _cam.IsOpen())
            {
                _cam.Stop();
                _logger.Debug("Camera closed!");
            }
        }

        private void btnAddRoi_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_curImage == null)
                return;

            if (_addingRoi)
            {
                SetAddRoiButtonNormal();
                imageViewer.CurrentTool = DrawingTool.Select;
                _addingRoi = false;
                return;
            }

            SetAddRoiButtonActive();
            imageViewer.CurrentTool = DrawingTool.RotatedRectangle;
            imageViewer.Focus();
            _addingRoi = true;
        }

        private void btnRemoveRoi_MouseDown(object sender, MouseButtonEventArgs e)
        {
            imageViewer.ClearShapes();
            _curTempImage = null;
            imbTemplate.Source = null;
            ResetRoiInfo();
            OnPropertyChanged(nameof(CanAddRemove));
        }

        private void btnAddTemplate_MouseDown(object sender, MouseButtonEventArgs e)
        {
            VisionShape roi = GetCurrentRotatedRectangle();
            if (roi == null || _curTempImage == null)
            {
                var error = new ErrorWindow("Please create a rotated ROI first!\rHãy tạo ROI xoay trước!");
                error.ShowDialog();
                return;
            }

            // Đây là thông số mày cần để lưu master point.
            // Nếu Template/ModelInfo của mày có field riêng thì gán thêm tại đây.
            MasterRoiInfo roiInfo = GetCurrentMasterRoiInfo();
            _logger.Info(string.Format(
                "Master ROI: Cx={0:F3}, Cy={1:F3}, W={2:F3}, H={3:F3}, Angle={4:F3}",
                roiInfo.Cx, roiInfo.Cy, roiInfo.Width, roiInfo.Height, roiInfo.AngleDeg));

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var template = new Models.Template(
                TemplatesList.Count,
                _curTempImage,
                string.Format("{0}/{1}/{2}.png", _param.MODELS_PATH, _model.Name, timestamp));

            TemplatesList.Add(template);

            CanSave = true;
            OnPropertyChanged(nameof(CanSave));
        }

        private void btnCancel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var warning = new WarningWindow("Are you sure to cancel processes?");
            bool? res = warning.ShowDialog();
            if (res == true)
                Close();
        }

        private void btnSave_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _model.Templates = TemplatesList.ToList();

            // Nếu cần lưu master point vào ModelInfo thì thêm field vào ModelInfo rồi gán ở đây.
            // Ví dụ:
            // MasterRoiInfo roi = GetCurrentMasterRoiInfo();
            // _model.MasterPointImageX = roi.Cx;
            // _model.MasterPointImageY = roi.Cy;
            // _model.MasterPointAngleDeg = roi.AngleDeg;

            _modelsWindow.UpdateModel(_model);

            var info = new InformationWindow("Save successfully!");
            info.ShowDialog();
            Close();
        }

        private void udtbAngle_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9.-]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void udtbAngle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_updatingAngleFromViewer)
                return;

            VisionShape roi = GetCurrentRotatedRectangle();
            if (roi == null)
                return;

            double value;
            try
            {
                value = Convert.ToDouble(udtbAngle.Text);
            }
            catch
            {
                return;
            }

            roi.AngleDeg = value;
            imageViewer.InvalidateVisual();
            UpdateCurrentRoiFromViewer();
        }

        private void ImageViewer_ShapeCreated(object sender, ShapeEventArgs e)
        {
            if (e == null || e.Shape == null)
                return;

            // Cửa sổ này chỉ cần 1 ROI master dạng rotated rectangle.
            if (e.Shape.Kind != VisionShapeKind.RotatedRectangle)
            {
                imageViewer.Shapes.Remove(e.Shape);
                return;
            }

            // Chỉ giữ ROI mới nhất.
            List<VisionShape> removeList = new List<VisionShape>();
            foreach (VisionShape shape in imageViewer.Shapes)
            {
                if (!object.ReferenceEquals(shape, e.Shape))
                    removeList.Add(shape);
            }

            for (int i = 0; i < removeList.Count; i++)
                imageViewer.Shapes.Remove(removeList[i]);

            imageViewer.CurrentTool = DrawingTool.Select;
            _addingRoi = false;
            SetAddRoiButtonNormal();

            UpdateCurrentRoiFromViewer();
            OnPropertyChanged(nameof(CanAddRemove));
        }

        private void ImageViewer_ShapeChanged(object sender, ShapeEventArgs e)
        {
            UpdateCurrentRoiFromViewer();
        }

        private void ImageViewer_ShapeSelectionChanged(object sender, EventArgs e)
        {
            UpdateCurrentRoiFromViewer();
        }

        private VisionShape GetCurrentRotatedRectangle()
        {
            if (imageViewer == null)
                return null;

            VisionShape selected = imageViewer.SelectedShape;
            if (selected != null && selected.Kind == VisionShapeKind.RotatedRectangle)
                return selected;

            foreach (VisionShape shape in imageViewer.Shapes)
            {
                if (shape.Kind == VisionShapeKind.RotatedRectangle)
                    return shape;
            }

            return null;
        }

        private void UpdateCurrentRoiFromViewer()
        {
            VisionShape roi = GetCurrentRotatedRectangle();
            if (roi == null || roi.IsEmpty)
            {
                _curTempImage = null;
                if (SelectedTemplate == null)
                    imbTemplate.Source = null;

                ResetRoiInfo();
                OnPropertyChanged(nameof(CanAddRemove));
                return;
            }

            RoiCx = Math.Round(roi.Cx, 3);
            RoiCy = Math.Round(roi.Cy, 3);
            RoiWidth = Math.Round(roi.Width, 3);
            RoiHeight = Math.Round(roi.Height, 3);
            RoiAngleDeg = Math.Round(roi.AngleDeg, 3);

            _updatingAngleFromViewer = true;
            try
            {
                udtbAngle.Value = RoiAngleDeg;
            }
            finally
            {
                _updatingAngleFromViewer = false;
            }

            UpdateTemplatePreviewByRoi(roi);
            OnPropertyChanged(nameof(CanAddRemove));
        }

        private void UpdateTemplatePreviewByRoi(VisionShape roi)
        {
            if (_curImage == null || roi == null || roi.IsEmpty)
                return;

            try
            {
                Image<Bgr, byte> cropped = CropRotatedRectangle(_curImage, roi);

                if (_curTempImage != null)
                    _curTempImage.Dispose();

                _curTempImage = cropped;
                imbTemplate.Source = Converter.BitmapToBitmapSource(_curTempImage.Bitmap);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Cannot crop rotated ROI preview.");
            }
        }

        private static Image<Bgr, byte> CropRotatedRectangle(Image<Bgr, byte> src, VisionShape roi)
        {
            int outW = Math.Max(1, Convert.ToInt32(Math.Round(roi.Width)));
            int outH = Math.Max(1, Convert.ToInt32(Math.Round(roi.Height)));

            System.Windows.Point[] pts = roi.GetRotatedRectanglePoints();

            PointF[] srcPts = new PointF[4];
            srcPts[0] = new PointF((float)pts[0].X, (float)pts[0].Y);
            srcPts[1] = new PointF((float)pts[1].X, (float)pts[1].Y);
            srcPts[2] = new PointF((float)pts[2].X, (float)pts[2].Y);
            srcPts[3] = new PointF((float)pts[3].X, (float)pts[3].Y);

            PointF[] dstPts = new PointF[4];
            dstPts[0] = new PointF(0, 0);
            dstPts[1] = new PointF(outW - 1, 0);
            dstPts[2] = new PointF(outW - 1, outH - 1);
            dstPts[3] = new PointF(0, outH - 1);

            Mat transform = CvInvoke.GetPerspectiveTransform(srcPts, dstPts);
            Image<Bgr, byte> dst = new Image<Bgr, byte>(outW, outH);

            CvInvoke.WarpPerspective(
                src,
                dst,
                transform,
                new System.Drawing.Size(outW, outH),
                Inter.Linear,
                Warp.Default,
                BorderType.Constant,
                new MCvScalar(0, 0, 0));

            transform.Dispose();
            return dst;
        }

        public MasterRoiInfo GetCurrentMasterRoiInfo()
        {
            VisionShape roi = GetCurrentRotatedRectangle();
            if (roi == null)
                return null;

            MasterRoiInfo info = new MasterRoiInfo();
            info.Cx = roi.Cx;
            info.Cy = roi.Cy;
            info.Width = roi.Width;
            info.Height = roi.Height;
            info.AngleDeg = roi.AngleDeg;

            System.Windows.Point[] pts = roi.GetRotatedRectanglePoints();
            info.Points = pts;

            return info;
        }

        private void ResetRoiInfo()
        {
            RoiCx = 0;
            RoiCy = 0;
            RoiWidth = 0;
            RoiHeight = 0;
            RoiAngleDeg = 0;

            _updatingAngleFromViewer = true;
            try
            {
                udtbAngle.Value = 0;
            }
            finally
            {
                _updatingAngleFromViewer = false;
            }
        }

        private void SetAddRoiButtonActive()
        {
            btnAddRoi.StartColor = Colors.DarkOrange;
            btnAddRoi.EndColor = Colors.DarkOrange;
            Cursor = Cursors.Cross;
        }

        private void SetAddRoiButtonNormal()
        {
            btnAddRoi.StartColor = Color.FromRgb(0xCC, 0xFF, 0xAA);
            btnAddRoi.EndColor = Color.FromRgb(0x1E, 0x5B, 0x53);
            Cursor = Cursors.Arrow;
        }

        private void dgTemplatesList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
                return;

            if (TemplatesList.Count == 0 || SelectedTemplate == null)
                return;

            var warning = new WarningWindow(string.Format("Are you sure to delete Template {0}?", SelectedTemplate.Id));
            bool? res = warning.ShowDialog();
            if (res != true)
                return;

            TemplatesList.Remove(SelectedTemplate);
            SelectedTemplate = null;

            for (int i = 0; i < TemplatesList.Count; i++)
                TemplatesList[i].Id = i;

            CanSave = true;
            OnPropertyChanged(nameof(CanSave));
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Phím tắt chính đã được DLL ImageViewer xử lý khi viewer đang focus:
            // Arrow: move ROI
            // Ctrl + Arrow: resize ROI
            // Alt + Left/Right: rotate ROI
            // Delete: delete ROI
            // Esc: select mode
            // Giữ hàm này để không lỗi XAML cũ.
        }
    }

    public class MasterRoiInfo
    {
        public double Cx { get; set; }
        public double Cy { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double AngleDeg { get; set; }
        public System.Windows.Point[] Points { get; set; }
    }
}

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using LincolnVision.ImageViewer;
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
    /// Window setup / edit / test template master.
    ///
    /// Luồng chính:
    /// 1. Load ảnh master của model.
    /// 2. Select template cũ -> vẽ lại ROI đã lưu.
    /// 3. User kéo / resize / rotate ROI -> đánh dấu template đang dirty.
    /// 4. Save hoặc chuyển sang Test -> commit template dirty:
    ///    - crop lại image template
    ///    - transform center ROI sang robot
    ///    - transform direction point sang robot
    ///    - tính lại MasterRobotAngle
    ///    - tính lại OffsetX / OffsetY / OffsetRZ
    ///    - replace template cũ trong TemplatesList
    /// 5. Save model.
    ///
    /// Lưu ý:
    /// - Code giữ C# 7.3 compatible.
    /// - Không dùng SelectedShape setter vì DLL đang expose SelectedShape dạng read-only.
    /// - Tách _setupImage và _testImage để tránh test nhầm ảnh master.
    /// </summary>
    public partial class TemplatesSettingWindow : Window, INotifyPropertyChanged
    {
        #region Constants

        private const double PropertyTolerance = 0.0001;
        private const double RoiCompareTolerance = 0.001;

        /// <summary>
        /// Độ dài vector hướng tính bằng pixel.
        /// Dùng để tạo điểm thứ 2 theo hướng Angle của ROI.
        /// Sau đó transform 2 điểm image sang robot để tính góc object trong hệ robot.
        /// </summary>
        private const double DirectionLengthPixel = 100.0;

        #endregion

        #region Fields

        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly Properties.Settings _param = Properties.Settings.Default;
        private readonly ModelInfo _model;
        private readonly ModelsManagerWindow _modelsWindow;
        private readonly Models.RobotPose _robotPose;

        private CameraManager _cameraManager;
        private LincolnCamera _cam;
        private DobotRobotClient _robot;

        /// <summary>
        /// Ảnh master/setup của model.
        /// ROI template luôn được vẽ và crop trên ảnh này.
        /// </summary>
        private Image<Bgr, byte> _setupImage;

        /// <summary>
        /// Ảnh dùng riêng cho tab Test.
        /// Không dùng chung với _setupImage để tránh nhầm ảnh khi test.
        /// </summary>
        private Image<Bgr, byte> _testImage;

        /// <summary>
        /// Ảnh template preview hiện tại, được crop từ ROI hiện tại.
        /// Khi add/update template sẽ clone ảnh này để lưu vào Template.Image.
        /// </summary>
        private Image<Bgr, byte> _curTempImage;

        /// <summary>
        /// True khi user đang ở chế độ vẽ ROI mới.
        /// </summary>
        private bool _addingRoi;

        /// <summary>
        /// True khi code đang update udtbAngle từ ROI.
        /// Dùng để tránh ValueChanged gọi ngược lại UpdateCurrentRoiFromViewer.
        /// </summary>
        private bool _updatingAngleFromViewer;

        /// <summary>
        /// True khi code đang load/vẽ lại ROI cũ từ template.
        /// Dùng để tránh select template bị hiểu nhầm là user đã sửa ROI.
        /// </summary>
        private bool _suppressRoiDirty;

        /// <summary>
        /// True khi ROI của SelectedTemplate đã bị user thay đổi.
        /// Khi Save/Test sẽ commit lại template này.
        /// </summary>
        private bool _selectedTemplateDirty;

        #endregion

        #region Binding Properties

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<Template> TemplatesList { get; private set; }

        private Template _selectedTemplate;
        public Template SelectedTemplate
        {
            get { return _selectedTemplate; }
            set
            {
                if (!object.ReferenceEquals(_selectedTemplate, value))
                {
                    _selectedTemplate = value;
                    OnPropertyChanged(nameof(SelectedTemplate));
                    OnTemplateSelectionChanged();
                }
            }
        }

        private double _roiCx;
        public double RoiCx
        {
            get { return _roiCx; }
            set { SetDoubleProperty(ref _roiCx, value, nameof(RoiCx)); }
        }

        private double _roiCy;
        public double RoiCy
        {
            get { return _roiCy; }
            set { SetDoubleProperty(ref _roiCy, value, nameof(RoiCy)); }
        }

        private double _roiWidth;
        public double RoiWidth
        {
            get { return _roiWidth; }
            set { SetDoubleProperty(ref _roiWidth, value, nameof(RoiWidth)); }
        }

        private double _roiHeight;
        public double RoiHeight
        {
            get { return _roiHeight; }
            set { SetDoubleProperty(ref _roiHeight, value, nameof(RoiHeight)); }
        }

        private double _roiAngleDeg;
        public double RoiAngleDeg
        {
            get { return _roiAngleDeg; }
            set { SetDoubleProperty(ref _roiAngleDeg, value, nameof(RoiAngleDeg)); }
        }

        private bool _cameraConnected;
        public bool CameraConnected
        {
            get { return _cameraConnected; }
            private set { SetBoolProperty(ref _cameraConnected, value, nameof(CameraConnected)); }
        }

        private bool _canSave;
        public bool CanSave
        {
            get { return _canSave; }
            private set { SetBoolProperty(ref _canSave, value, nameof(CanSave)); }
        }

        private bool _canProcesImage;
        public bool CanProcesImage
        {
            get { return _canProcesImage; }
            private set { SetBoolProperty(ref _canProcesImage, value, nameof(CanProcesImage)); }
        }

        /// <summary>
        /// XAML hiện tại đang bind CanFinish cho nút Connect Camera.
        /// Nếu XAML vẫn giữ IsEnabled="{Binding CanFinish}" thì cần property này.
        /// </summary>
        public bool CanFinish
        {
            get { return true; }
        }

        public bool CanAddRemove
        {
            get { return GetCurrentRotatedRectangle() != null; }
        }

        #endregion

        #region Constructor / Init

        public TemplatesSettingWindow(
            ModelsManagerWindow window,
            ModelInfo model,
            Models.RobotPose robotPose)
        {
            InitializeComponent();

            _modelsWindow = window;
            _model = model;
            _robotPose = robotPose;

            TemplatesList = new ObservableCollection<Template>();

            DataContext = this;

            Init();
        }

        private void Init()
        {
            _cameraManager = CameraManager.GetInstance();

            LoadCameraSerialNumbers();
            LoadTemplatesFromModel();
            InitImageViewer();
            LoadSetupImageFromModel();

            if (TemplatesList.Count > 0)
                SelectedTemplate = TemplatesList[0];
            else
                ResetRoiInfo();

            CanSave = false;
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanAddRemove));
        }

        private void LoadCameraSerialNumbers()
        {
            cbbCamSn.Items.Clear();

            List<CamInfo> camInfoList = LincolnCamera.GetListCamInfo();
            for (int i = 0; i < camInfoList.Count; i++)
            {
                cbbCamSn.Items.Add(camInfoList[i].SN);
            }
        }

        private void LoadTemplatesFromModel()
        {
            TemplatesList.Clear();

            if (_model == null || _model.Templates == null)
                return;

            for (int i = 0; i < _model.Templates.Count; i++)
            {
                _model.Templates[i].Id = i;
                TemplatesList.Add(_model.Templates[i]);
            }
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

        private void LoadSetupImageFromModel()
        {
            if (_model == null || _model.BigImage == null)
                return;

            _setupImage = new Image<Bgr, byte>(_model.BigImage.Bitmap);
            LoadImageToViewer(_setupImage);

            CanProcesImage = true;
            OnPropertyChanged(nameof(CanProcesImage));
        }

        #endregion

        #region Template Selection / Draw ROI

        /// <summary>
        /// Khi user chọn template trên DataGrid:
        /// - Không được đánh dấu dirty.
        /// - Load preview template.
        /// - Vẽ lại ROI đã lưu lên ảnh master.
        /// </summary>
        private void OnTemplateSelectionChanged()
        {
            _suppressRoiDirty = true;

            try
            {
                imageViewer.ClearShapes();
                _selectedTemplateDirty = false;

                if (SelectedTemplate == null)
                {
                    ClearTemplatePreview();
                    ResetRoiInfo();
                    OnPropertyChanged(nameof(CanAddRemove));
                    return;
                }

                EnsureSelectedTemplateImageLoaded();
                ShowSelectedTemplatePreview();
                DrawSelectedTemplateRoi();
            }
            finally
            {
                _suppressRoiDirty = false;
            }
        }

        private void EnsureSelectedTemplateImageLoaded()
        {
            if (SelectedTemplate == null)
                return;

            if (SelectedTemplate.Image != null)
                return;

            if (string.IsNullOrEmpty(SelectedTemplate.ImagePath))
                return;

            if (!System.IO.File.Exists(SelectedTemplate.ImagePath))
                return;

            SelectedTemplate.Image = new Image<Bgr, byte>(SelectedTemplate.ImagePath);
        }

        private void ShowSelectedTemplatePreview()
        {
            if (SelectedTemplate != null && SelectedTemplate.Image != null)
                imbTemplate.Source = Converter.BitmapToBitmapSource(SelectedTemplate.Image.Bitmap);
            else
                imbTemplate.Source = null;
        }

        /// <summary>
        /// Vẽ ROI đã lưu của SelectedTemplate lên ảnh master.
        /// </summary>
        private void DrawSelectedTemplateRoi()
        {
            if (SelectedTemplate == null || imageViewer == null)
                return;

            if (_setupImage == null)
                LoadSetupImageFromModel();

            if (_setupImage == null)
                return;

            LoadImageToViewer(_setupImage);
            imageViewer.ClearShapes();

            VisionShape roi = new VisionShape
            {
                Kind = VisionShapeKind.RotatedRectangle,
                Name = string.Format("Template_{0}_ROI", SelectedTemplate.Id),
                Stroke = System.Windows.Media.Brushes.Lime,
                Fill = System.Windows.Media.Brushes.Transparent,
                StrokeThickness = 2,

                Cx = SelectedTemplate.CenterX,
                Cy = SelectedTemplate.CenterY,
                Width = SelectedTemplate.Width,
                Height = SelectedTemplate.Height,
                AngleDeg = SelectedTemplate.Angle
            };

            imageViewer.AddShape(roi);
            imageViewer.CurrentTool = DrawingTool.Select;
            imageViewer.Focus();

            UpdateCurrentRoiFromViewer();
            OnPropertyChanged(nameof(CanAddRemove));
        }

        #endregion

        #region Camera

        private async void btnConnectCamera_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (cbbCamSn.SelectedItem == null)
            {
                ShowError("Please choose a camera!\rHãy chọn camera!");
                return;
            }

            string camSn = cbbCamSn.SelectedValue.ToString();

            try
            {
                LincolnCamera openedCamera = await Task.Run(() =>
                {
                    if (_cam != null && _cam.IsOpen())
                        return _cam;

                    LincolnCamera camera = _cameraManager.GetCamera(camSn) as LincolnCamera;

                    if (camera == null || !camera.IsOpen())
                        throw new Exception(string.Format("Cannot open camera {0}!", camSn));

                    camera.SetWorkMode(CameraWorkMode.SoftwareTrigger);
                    camera.Start();

                    return camera;
                });

                _cam = openedCamera;
                CameraConnected = true;
            }
            catch (Exception ex)
            {
                ShowError(string.Format("{0}\rKhông mở được camera {1}!", ex.Message, camSn));
            }
        }

        /// <summary>
        /// Capture ảnh:
        /// - Setup tab: ảnh capture trở thành ảnh master mới.
        /// - Test tab: ảnh capture trở thành ảnh test riêng.
        /// </summary>
        private void btnCaptureImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Bitmap bitmap = TryCaptureBitmapFromCamera();
            if (bitmap == null)
                return;

            if (tabSetup.IsSelected)
                LoadNewSetupImage(bitmap);
            else
                LoadNewTestImage(bitmap);
        }

        private Bitmap TryCaptureBitmapFromCamera()
        {
            if (_cam == null || !_cam.IsOpen())
            {
                ShowError("Camera is not connected!\rCamera chưa được kết nối!");
                return null;
            }

            try
            {
                Bitmap bitmap = _cam.TriggerAndGetFrame();

                if (bitmap == null)
                {
                    ShowError("Cannot capture image!\rKhông chụp được ảnh!");
                    return null;
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                ShowError("Cannot capture image!\rKhông chụp được ảnh!\r" + ex.Message);
                return null;
            }
        }

        private void LoadNewSetupImage(Bitmap bitmap)
        {
            _setupImage = new Image<Bgr, byte>(bitmap);
            _testImage = null;

            ClearTemplatePreview();
            imageViewer.ClearShapes();
            LoadBitmapToViewer(bitmap);

            SelectedTemplate = null;
            ResetRoiInfo();

            CanProcesImage = true;
            SetCanSave(true);
            OnPropertyChanged(nameof(CanProcesImage));
            OnPropertyChanged(nameof(CanAddRemove));
        }

        private void LoadNewTestImage(Bitmap bitmap)
        {
            _testImage = new Image<Bgr, byte>(bitmap);

            imageViewer.ClearShapes();
            LoadBitmapToViewer(bitmap);

            CanProcesImage = true;
            OnPropertyChanged(nameof(CanProcesImage));
            OnPropertyChanged(nameof(CanAddRemove));
        }

        #endregion

        #region Window Events

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            imageViewer.Focus();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                if (_cam != null && _cam.IsOpen())
                {
                    _cam.Stop();
                    _logger.Debug("Camera stopped.");
                }

                if (_robot != null)
                {
                    _robot.Dispose();
                    _robot = null;
                }

                if (_curTempImage != null)
                {
                    _curTempImage.Dispose();
                    _curTempImage = null;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error while closing TemplatesSettingWindow.");
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Các phím tắt chính đã được ImageViewer DLL xử lý:
            // Arrow           : move ROI
            // Ctrl + Arrow    : resize ROI
            // Alt + Left/Right: rotate ROI
            // Delete          : delete ROI
            // Esc             : select mode
            //
            // Giữ handler này vì XAML đang khai báo PreviewKeyDown.
        }

        #endregion

        #region ROI Buttons

        private void btnAddRoi_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_setupImage == null)
                return;

            if (_addingRoi)
            {
                StopAddRoiMode();
                return;
            }

            StartAddRoiMode();
        }

        private void btnRemoveRoi_MouseDown(object sender, MouseButtonEventArgs e)
        {
            imageViewer.ClearShapes();
            ClearTemplatePreview();
            ResetRoiInfo();

            OnPropertyChanged(nameof(CanAddRemove));
        }

        private void StartAddRoiMode()
        {
            SetAddRoiButtonActive();

            imageViewer.CurrentTool = DrawingTool.RotatedRectangle;
            imageViewer.Focus();

            _addingRoi = true;
        }

        private void StopAddRoiMode()
        {
            SetAddRoiButtonNormal();

            imageViewer.CurrentTool = DrawingTool.Select;
            imageViewer.Focus();

            _addingRoi = false;
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

        #endregion

        #region Add / Commit / Save Template

        /// <summary>
        /// Add template mới từ ROI hiện tại.
        /// </summary>
        private void btnAddTemplate_MouseDown(object sender, MouseButtonEventArgs e)
        {
            string imagePath = BuildNewTemplateImagePath();

            Template template;
            if (!TryBuildTemplateFromCurrentRoi(TemplatesList.Count, imagePath, out template))
                return;

            TemplatesList.Add(template);
            SelectedTemplate = template;

            _selectedTemplateDirty = false;
            SetCanSave(true);
        }

        private void btnSave_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!CommitSelectedTemplateEditIfNeeded())
                return;

            if (TemplatesList.Count == 0)
            {
                ShowError("Template list is empty!\rDanh sách template đang trống!");
                return;
            }

            _model.Templates = TemplatesList.ToList();

            if (_setupImage != null)
            {
                _model.BigImage = _setupImage;
                _model.BigImagePath = string.Format(
                    "{0}/{1}/{2}.png",
                    _param.MODELS_PATH,
                    _model.Name,
                    "bigImage");
            }

            _model.PickPose = _robotPose;

            _modelsWindow.UpdateModel(_model);

            _selectedTemplateDirty = false;
            SetCanSave(false);

            new InformationWindow("Save successfully!").ShowDialog();
        }

        private void btnCancel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            WarningWindow warning = new WarningWindow("Are you sure to cancel processes?");
            bool? res = warning.ShowDialog();

            if (res == true)
                Close();
        }

        /// <summary>
        /// Nếu SelectedTemplate đã bị user sửa ROI thì build lại template mới
        /// và replace đúng item cũ trong TemplatesList.
        /// </summary>
        private bool CommitSelectedTemplateEditIfNeeded()
        {
            if (SelectedTemplate == null)
                return true;

            if (!_selectedTemplateDirty)
                return true;

            int index = TemplatesList.IndexOf(SelectedTemplate);
            if (index < 0)
                return true;

            string imagePath = SelectedTemplate.ImagePath;

            if (string.IsNullOrEmpty(imagePath))
                imagePath = BuildNewTemplateImagePath();

            Template updatedTemplate;
            if (!TryBuildTemplateFromCurrentRoi(SelectedTemplate.Id, imagePath, out updatedTemplate))
                return false;

            TemplatesList[index] = updatedTemplate;

            _suppressRoiDirty = true;
            try
            {
                SelectedTemplate = updatedTemplate;
            }
            finally
            {
                _suppressRoiDirty = false;
            }

            _selectedTemplateDirty = false;

            return true;
        }

        /// <summary>
        /// Build Template từ ROI hiện tại.
        ///
        /// Hàm này là core của setup:
        /// - đọc ROI hiện tại
        /// - crop ảnh template
        /// - transform center sang robot
        /// - tạo điểm hướng theo angle ROI
        /// - transform điểm hướng sang robot
        /// - tính angle object trong hệ robot
        /// - tính offset local từ object center tới pose gắp
        /// </summary>
        private bool TryBuildTemplateFromCurrentRoi(
            int templateId,
            string imagePath,
            out Template template)
        {
            template = null;

            _logger.Info("========== TEMPLATE BUILD DEBUG BEGIN ==========");
            _logger.Info(string.Format(
                "TemplateId={0}, ImagePath={1}",
                templateId,
                imagePath));

            if (_robotPose == null)
            {
                _logger.Error("Robot pick pose is null while building template.");
                _logger.Info("========== TEMPLATE BUILD DEBUG END: FAILED ==========");
                ShowError("Robot pick pose is null!\rPose gắp của robot chưa hợp lệ!");
                return false;
            }

            VisionShape roi = GetCurrentRotatedRectangle();

            if (roi == null || roi.IsEmpty || _curTempImage == null)
            {
                _logger.Error(string.Format(
                    "Invalid ROI or template image. RoiNull={0}, RoiEmpty={1}, CurTempImageNull={2}",
                    roi == null,
                    roi != null && roi.IsEmpty,
                    _curTempImage == null));
                _logger.Info("========== TEMPLATE BUILD DEBUG END: FAILED ==========");
                ShowError("Please create a rotated ROI first!\rHãy tạo ROI xoay trước!");
                return false;
            }

            MasterRoiInfo roiInfo = GetCurrentMasterRoiInfo();

            if (roiInfo == null)
            {
                _logger.Error("GetCurrentMasterRoiInfo returned null.");
                _logger.Info("========== TEMPLATE BUILD DEBUG END: FAILED ==========");
                ShowError("Invalid ROI!\rROI không hợp lệ!");
                return false;
            }

            _logger.Info(string.Format(
                "ROI Image: Cx={0:F3}, Cy={1:F3}, W={2:F3}, H={3:F3}, Angle={4:F3}",
                roiInfo.Cx,
                roiInfo.Cy,
                roiInfo.Width,
                roiInfo.Height,
                roiInfo.AngleDeg));

            LogRoiPoints(roiInfo);

            double realRobotCenterX;
            double realRobotCenterY;

            if (!TryTransformImagePointToRobot(
                roiInfo.Cx,
                roiInfo.Cy,
                "Cannot transform master center point to real coordinate, check calibration!\rKhông chuyển được tâm master sang tọa độ robot, kiểm tra calibration!",
                out realRobotCenterX,
                out realRobotCenterY))
            {
                _logger.Info("========== TEMPLATE BUILD DEBUG END: FAILED ==========");
                return false;
            }

            _logger.Info(string.Format(
                "Transform Center: Image=({0:F3}, {1:F3}) -> Robot=({2:F3}, {3:F3})",
                roiInfo.Cx,
                roiInfo.Cy,
                realRobotCenterX,
                realRobotCenterY));

            // ROI Angle của ImageViewer và result.angle của matcher đang ngược dấu.
            // Runtime dùng result.angle, nên setup master cũng phải dùng cùng convention với matcher.
            double masterImageAngleForRobot = -roiInfo.AngleDeg;

            double theta = masterImageAngleForRobot * Math.PI / 180.0;

            double directionImageX = roiInfo.Cx + DirectionLengthPixel * Math.Cos(theta);
            double directionImageY = roiInfo.Cy - DirectionLengthPixel * Math.Sin(theta);

            double imageDirDx = directionImageX - roiInfo.Cx;
            double imageDirDy = directionImageY - roiInfo.Cy;

            _logger.Info(string.Format(
                "Direction Image: X={0:F3}, Y={1:F3}, dX={2:F3}, dY={3:F3}, Len={4:F3}, AngleInput={5:F3}",
                directionImageX,
                directionImageY,
                imageDirDx,
                imageDirDy,
                VectorLength(imageDirDx, imageDirDy),
                masterImageAngleForRobot));

            double directionRobotX;
            double directionRobotY;

            if (!TryTransformImagePointToRobot(
                directionImageX,
                directionImageY,
                "Cannot transform master direction point to real coordinate, check calibration!\rKhông chuyển được điểm hướng của master sang tọa độ robot, kiểm tra calibration!",
                out directionRobotX,
                out directionRobotY))
            {
                _logger.Info("========== TEMPLATE BUILD DEBUG END: FAILED ==========");
                return false;
            }

            double robotDirDx = directionRobotX - realRobotCenterX;
            double robotDirDy = directionRobotY - realRobotCenterY;

            _logger.Info(string.Format(
                "Transform Direction: Image=({0:F3}, {1:F3}) -> Robot=({2:F3}, {3:F3})",
                directionImageX,
                directionImageY,
                directionRobotX,
                directionRobotY));

            _logger.Info(string.Format(
                "Robot Direction Vector: dX={0:F3}, dY={1:F3}, Len={2:F3}, AngleByVector={3:F3}",
                robotDirDx,
                robotDirDy,
                VectorLength(robotDirDx, robotDirDy),
                VectorAngleDeg(robotDirDx, robotDirDy)));

            double masterRobotAngle;

            try
            {
                masterRobotAngle = Models.Template.CalculateRobotAngleFromTwoRobotPoints(
                    realRobotCenterX,
                    realRobotCenterY,
                    directionRobotX,
                    directionRobotY);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Cannot calculate master robot angle.");
                _logger.Info("========== TEMPLATE BUILD DEBUG END: FAILED ==========");
                ShowError("Cannot calculate master robot angle!\rKhông tính được góc master trong hệ robot!\r" + ex.Message);
                return false;
            }

            _logger.Info(string.Format(
                "MasterRobotAngle Calculated: {0:F3}",
                masterRobotAngle));

            _logger.Info(string.Format(
                "Teach Pose Used For Offset: X={0:F3}, Y={1:F3}, RZ={2:F3}",
                _robotPose.X,
                _robotPose.Y,
                _robotPose.Rz));

            double worldOffsetDx = _robotPose.X - realRobotCenterX;
            double worldOffsetDy = _robotPose.Y - realRobotCenterY;

            _logger.Info(string.Format(
                "World Offset Before Local: dX={0:F3}, dY={1:F3}, Len={2:F3}, Angle={3:F3}",
                worldOffsetDx,
                worldOffsetDy,
                VectorLength(worldOffsetDx, worldOffsetDy),
                VectorAngleDeg(worldOffsetDx, worldOffsetDy)));

            template = new Template(
                templateId,
                _curTempImage.Clone(),
                imagePath,
                roiInfo.Cx,
                roiInfo.Cy,
                roiInfo.Width,
                roiInfo.Height,
                roiInfo.AngleDeg);

            template.UpdateRealCoord(
                realRobotCenterX,
                realRobotCenterY,
                masterRobotAngle,
                _robotPose.X,
                _robotPose.Y,
                _robotPose.Rz);

            LogTemplateBuildResult(template);
            LogTemplateSetupVerification(template);

            _logger.Info("========== TEMPLATE BUILD DEBUG END: OK ==========");

            return true;
        }

        private bool TryTransformImagePointToRobot(
            double imageX,
            double imageY,
            string errorMessage,
            out double robotX,
            out double robotY)
        {
            robotX = 0;
            robotY = 0;

            var response = APICommunication.TransformPoint(_param.ApiUrlAi, imageX, imageY);

            if (response == null)
            {
                ShowError(errorMessage);
                return false;
            }

            try
            {
                robotX = Convert.ToDouble(response.RobotX);
                robotY = Convert.ToDouble(response.RobotY);
                return true;
            }
            catch (Exception ex)
            {
                ShowError(errorMessage + "\r" + ex.Message);
                return false;
            }
        }

        private string BuildNewTemplateImagePath()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

            return string.Format(
                "{0}/{1}/template_{2}.png",
                _param.MODELS_PATH,
                _model.Name,
                timestamp);
        }

        private void LogTemplateBuildResult(Template template)
        {
            if (template == null)
                return;

            _logger.Info(string.Format(
                "Build Template: Id={0}, Cx={1:F3}, Cy={2:F3}, W={3:F3}, H={4:F3}, Angle={5:F3}, RobotCenterX={6:F3}, RobotCenterY={7:F3}, MasterRobotAngle={8:F3}, OffsetX={9:F3}, OffsetY={10:F3}, OffsetRZ={11:F3}",
                template.Id,
                template.CenterX,
                template.CenterY,
                template.Width,
                template.Height,
                template.Angle,
                template.RealRobotCenterX,
                template.RealRobotCenterY,
                template.MasterRobotAngle,
                template.OffsetX,
                template.OffsetY,
                template.OffsetRZ));
        }

        #endregion

        #region Angle Input

        private void udtbAngle_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Chỉ cho nhập số, dấu âm và dấu chấm.
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
            if (!TryGetAngleFromInput(out value))
                return;

            roi.AngleDeg = value;

            imageViewer.InvalidateVisual();
            UpdateCurrentRoiFromViewer();
        }

        private bool TryGetAngleFromInput(out double value)
        {
            value = 0;

            try
            {
                if (udtbAngle.Value.HasValue)
                {
                    value = Convert.ToDouble(udtbAngle.Value.Value);
                    return true;
                }

                value = Convert.ToDouble(udtbAngle.Text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region ImageViewer Events

        private void ImageViewer_ShapeCreated(object sender, ShapeEventArgs e)
        {
            if (e == null || e.Shape == null)
                return;

            if (e.Shape.Kind != VisionShapeKind.RotatedRectangle)
            {
                imageViewer.Shapes.Remove(e.Shape);
                return;
            }

            KeepOnlyNewestShape(e.Shape);

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

        private void KeepOnlyNewestShape(VisionShape newestShape)
        {
            List<VisionShape> removeList = new List<VisionShape>();

            foreach (VisionShape shape in imageViewer.Shapes)
            {
                if (!object.ReferenceEquals(shape, newestShape))
                    removeList.Add(shape);
            }

            for (int i = 0; i < removeList.Count; i++)
                imageViewer.Shapes.Remove(removeList[i]);
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

        /// <summary>
        /// Đồng bộ thông tin ROI từ ImageViewer sang binding properties
        /// và cập nhật ảnh preview template.
        /// </summary>
        private void UpdateCurrentRoiFromViewer()
        {
            VisionShape roi = GetCurrentRotatedRectangle();

            if (roi == null || roi.IsEmpty)
            {
                ClearTemplatePreview();

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
            MarkSelectedTemplateDirtyIfNeeded(roi);

            OnPropertyChanged(nameof(CanAddRemove));
        }

        #endregion

        #region ROI Crop / ROI Info

        private void UpdateTemplatePreviewByRoi(VisionShape roi)
        {
            if (_setupImage == null || roi == null || roi.IsEmpty)
                return;

            try
            {
                Image<Bgr, byte> cropped = CropRotatedRectangle(_setupImage, roi);

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

            System.Windows.Point[] points = roi.GetRotatedRectanglePoints();

            PointF[] srcPts = new PointF[4];
            srcPts[0] = new PointF((float)points[0].X, (float)points[0].Y);
            srcPts[1] = new PointF((float)points[1].X, (float)points[1].Y);
            srcPts[2] = new PointF((float)points[2].X, (float)points[2].Y);
            srcPts[3] = new PointF((float)points[3].X, (float)points[3].Y);

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
            if (roi == null || roi.IsEmpty)
                return null;

            MasterRoiInfo info = new MasterRoiInfo
            {
                Cx = roi.Cx,
                Cy = roi.Cy,
                Width = roi.Width,
                Height = roi.Height,
                AngleDeg = roi.AngleDeg,
                Points = roi.GetRotatedRectanglePoints()
            };

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

        private void ClearTemplatePreview()
        {
            if (_curTempImage != null)
            {
                _curTempImage.Dispose();
                _curTempImage = null;
            }

            imbTemplate.Source = null;
        }

        #endregion

        #region DataGrid

        private void dgTemplatesList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
                return;

            if (TemplatesList.Count == 0 || SelectedTemplate == null)
                return;

            WarningWindow warning = new WarningWindow(
                string.Format("Are you sure to delete Template {0}?", SelectedTemplate.Id));

            bool? res = warning.ShowDialog();
            if (res != true)
                return;

            TemplatesList.Remove(SelectedTemplate);
            SelectedTemplate = null;

            ReindexTemplates();

            _selectedTemplateDirty = false;
            SetCanSave(true);
        }

        private void ReindexTemplates()
        {
            for (int i = 0; i < TemplatesList.Count; i++)
                TemplatesList[i].Id = i;
        }

        #endregion

        #region Test

        private async void btnTest_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!CommitSelectedTemplateEditIfNeeded())
                return;

            if (_testImage == null)
            {
                ShowError("Please capture a test image first!\rHãy chụp ảnh test trước!");
                return;
            }

            List<Image<Bgr, byte>> templateImages;
            List<List<double>> offsets;

            if (!TryBuildTestInput(out templateImages, out offsets))
                return;

            _logger.Info("========== TEST API CALL DEBUG BEGIN ==========");
            _logger.Info(string.Format(
                "ApiUrl={0}, TestImageNull={1}, TemplateCount={2}, OffsetCount={3}",
                _param.ApiUrlAi,
                _testImage == null,
                templateImages.Count,
                offsets.Count));
            _logger.Info("========== TEST API CALL DEBUG END ==========");

            var res = await APICommunication.TestGetRealCoord(
                _param.ApiUrlAi,
                _testImage,
                templateImages,
                offsets);

            if (res == null)
            {
                _logger.Error("TestGetRealCoord returned null.");
                ShowError("INTERNAL ERROR: Cannot Calculate Real Coordinates");
                return;
            }

            _logger.Info("========== TEST API RESULT DEBUG BEGIN ==========");
            _logger.Info(string.Format(
                "API Result Raw: Result={0}, Message={1}",
                res.Result,
                res.Message));
            _logger.Info(string.Format(
                "API Result Pose: ImgX={0}, ImgY={1}, ImgAngle={2}, RobotX={3}, RobotY={4}, RobotRZ={5}",
                res.ImageX != null ? res.ImageX.Value.ToString("F3") : "null",
                res.ImageY != null ? res.ImageY.Value.ToString("F3") : "null",
                res.ImageAngle != null ? res.ImageAngle.Value.ToString("F3") : "null",
                res.RobotX != null ? res.RobotX.Value.ToString("F3") : "null",
                res.RobotY != null ? res.RobotY.Value.ToString("F3") : "null",
                res.RobotAngle != null ? res.RobotAngle.Value.ToString("F3") : "null"));
            _logger.Info("========== TEST API RESULT DEBUG END ==========");

            if (!res.Result)
            {
                ShowError("ERROR: Cannot Find The Matching PCB Corner");
                return;
            }

            Bitmap resImg = Converter.Base64ToBitmap(res.ResImg);
            LoadBitmapToViewer(resImg);

            tbTestImageX.Text = res.ImageX != null ? res.ImageX.Value.ToString("F2") : string.Empty;
            tbTestImageY.Text = res.ImageY != null ? res.ImageY.Value.ToString("F2") : string.Empty;
            tbTestImageAngle.Text = res.ImageAngle != null ? res.ImageAngle.Value.ToString("F2") : string.Empty;

            tbTestRobotX.Text = res.RobotX != null ? res.RobotX.Value.ToString("F2") : string.Empty;
            tbTestRobotY.Text = res.RobotY != null ? res.RobotY.Value.ToString("F2") : string.Empty;
            tbTestRobotRz.Text = res.RobotAngle != null ? res.RobotAngle.Value.ToString("F2") : string.Empty;

            if (res.RobotX != null && res.RobotY != null && res.RobotAngle != null)
            {
                _logger.Info(string.Format(
                    "Move Robot To Test Position: X={0:F3}, Y={1:F3}, RZ={2:F3}",
                    Convert.ToDouble(res.RobotX),
                    Convert.ToDouble(res.RobotY),
                    Convert.ToDouble(res.RobotAngle)));

                await MoveRobotToTestPosition(
                    Convert.ToDouble(res.RobotX),
                    Convert.ToDouble(res.RobotY),
                    Convert.ToDouble(res.RobotAngle));
            }
        }

        private bool TryBuildTestInput(
            out List<Image<Bgr, byte>> templateImages,
            out List<List<double>> offsets)
        {
            templateImages = new List<Image<Bgr, byte>>();
            offsets = new List<List<double>>();

            _logger.Info("========== TEST INPUT DEBUG BEGIN ==========");

            foreach (Template template in TemplatesList.ToList())
            {
                if (template.Image == null)
                {
                    _logger.Warn(string.Format(
                        "Skip Template Id={0} because Image is null.",
                        template.Id));
                    continue;
                }

                templateImages.Add(template.Image);
                offsets.Add(new List<double>
                {
                    template.OffsetX,
                    template.OffsetY,
                    template.OffsetRZ
                });

                _logger.Info(string.Format(
                    "Send Template To API: Id={0}, Cx={1:F3}, Cy={2:F3}, W={3:F3}, H={4:F3}, ImgAngle={5:F3}, RobotCenterX={6:F3}, RobotCenterY={7:F3}, MasterRobotAngle={8:F3}",
                    template.Id,
                    template.CenterX,
                    template.CenterY,
                    template.Width,
                    template.Height,
                    template.Angle,
                    template.RealRobotCenterX,
                    template.RealRobotCenterY,
                    template.MasterRobotAngle));

                _logger.Info(string.Format(
                    "Send Offset To API: Id={0}, OffsetX={1:F3}, OffsetY={2:F3}, OffsetLen={3:F3}, OffsetAngle={4:F3}, OffsetRZ={5:F3}",
                    template.Id,
                    template.OffsetX,
                    template.OffsetY,
                    VectorLength(template.OffsetX, template.OffsetY),
                    VectorAngleDeg(template.OffsetX, template.OffsetY),
                    template.OffsetRZ));
            }

            _logger.Info(string.Format(
                "TemplateImages Count={0}, Offsets Count={1}",
                templateImages.Count,
                offsets.Count));

            _logger.Info("========== TEST INPUT DEBUG END ==========");

            if (templateImages.Count == 0)
            {
                ShowError("No valid template image!");
                return false;
            }

            return true;
        }

        private async Task<bool> CheckAndStartRobotAsync()
        {
            try
            {
                if (_robot != null && _robot.IsConnected())
                    return true;

                if (_robot != null)
                {
                    _robot.Dispose();
                    _robot = null;
                }

                _robot = new DobotRobotClient(
                    ipAddress: _param.RobotIp,
                    port: 8000,
                    timeoutMs: _param.ReadPoseTimeout);

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

                if (_robot != null)
                {
                    _robot.Dispose();
                    _robot = null;
                }

                return false;
            }
        }

        private async Task<bool> MoveRobotToTestPosition(double x, double y, double rz)
        {
            if (!await CheckAndStartRobotAsync())
            {
                ShowError("Robot is not connected!");
                return false;
            }

            try
            {
                return await _robot.TestAsync(x, y, rz);
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex.Message, "SYSTEM");
                ShowError("Cannot move robot, err: " + ex.Message);
                return false;
            }
        }

        #endregion

        #region Tab Control

        private void TabControl_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Tránh event SelectionChanged từ control con như ComboBox/DataGrid bubble lên TabControl.
            if (!object.ReferenceEquals(e.OriginalSource, sender))
                return;

            if (TabTest.IsSelected)
            {
                EnterTestMode();
            }
            else if (tabSetup.IsSelected)
            {
                EnterSetupMode();
            }
        }

        private void EnterTestMode()
        {
            // Rất quan trọng:
            // Nếu vừa sửa ROI ở tab Setup mà chuyển sang Test,
            // phải commit template trước khi clear shape.
            if (!CommitSelectedTemplateEditIfNeeded())
            {
                tabSetup.IsSelected = true;
                return;
            }

            ClearTestResultTextBoxes();

            imageViewer.ClearShapes();

            if (_testImage != null)
            {
                LoadImageToViewer(_testImage);
                CanProcesImage = true;
            }
            else
            {
                imageViewer.ImageSource = null;
                CanProcesImage = false;
            }

            OnPropertyChanged(nameof(CanProcesImage));
            OnPropertyChanged(nameof(CanAddRemove));
        }

        private void EnterSetupMode()
        {
            ClearTestResultTextBoxes();

            if (_setupImage != null)
            {
                LoadImageToViewer(_setupImage);
                CanProcesImage = true;
            }
            else
            {
                imageViewer.ImageSource = null;
                CanProcesImage = false;
            }

            if (SelectedTemplate != null)
                DrawSelectedTemplateRoi();
            else
                imageViewer.ClearShapes();

            OnPropertyChanged(nameof(CanProcesImage));
            OnPropertyChanged(nameof(CanAddRemove));
        }

        private void ClearTestResultTextBoxes()
        {
            tbTestImageX.Text = string.Empty;
            tbTestImageY.Text = string.Empty;
            tbTestImageAngle.Text = string.Empty;

            tbTestRobotX.Text = string.Empty;
            tbTestRobotY.Text = string.Empty;
            tbTestRobotRz.Text = string.Empty;
        }

        #endregion

        #region Dirty / Save State Helpers

        private bool IsSameRoiAsSelectedTemplate(VisionShape roi)
        {
            if (SelectedTemplate == null || roi == null)
                return true;

            return
                Math.Abs(roi.Cx - SelectedTemplate.CenterX) < RoiCompareTolerance &&
                Math.Abs(roi.Cy - SelectedTemplate.CenterY) < RoiCompareTolerance &&
                Math.Abs(roi.Width - SelectedTemplate.Width) < RoiCompareTolerance &&
                Math.Abs(roi.Height - SelectedTemplate.Height) < RoiCompareTolerance &&
                Math.Abs(Models.Template.NormalizeAngle(roi.AngleDeg - SelectedTemplate.Angle)) < RoiCompareTolerance;
        }

        private void MarkSelectedTemplateDirtyIfNeeded(VisionShape roi)
        {
            if (_suppressRoiDirty)
                return;

            if (SelectedTemplate == null)
                return;

            if (!IsSameRoiAsSelectedTemplate(roi))
            {
                _selectedTemplateDirty = true;
                SetCanSave(true);
            }
        }

        private void SetCanSave(bool value)
        {
            CanSave = value;
        }

        #endregion


        #region Debug Helper

        private static double VectorLength(double x, double y)
        {
            return Math.Sqrt(x * x + y * y);
        }

        private static double VectorAngleDeg(double x, double y)
        {
            if (Math.Abs(x) < 1e-9 && Math.Abs(y) < 1e-9)
                return 0;

            return Models.Template.NormalizeAngle(
                Math.Atan2(y, x) * 180.0 / Math.PI);
        }

        private static void RotateVectorForDebug(
            double x,
            double y,
            double angleDeg,
            out double rx,
            out double ry)
        {
            double theta = angleDeg * Math.PI / 180.0;

            double cos = Math.Cos(theta);
            double sin = Math.Sin(theta);

            rx = x * cos - y * sin;
            ry = x * sin + y * cos;
        }

        private void LogRoiPoints(MasterRoiInfo roiInfo)
        {
            if (roiInfo == null || roiInfo.Points == null)
                return;

            for (int i = 0; i < roiInfo.Points.Length; i++)
            {
                _logger.Info(string.Format(
                    "ROI Point[{0}]: X={1:F3}, Y={2:F3}",
                    i,
                    roiInfo.Points[i].X,
                    roiInfo.Points[i].Y));
            }
        }

        private void LogTemplateSetupVerification(Template template)
        {
            if (template == null || _robotPose == null)
                return;

            double verifyWorldDx;
            double verifyWorldDy;

            RotateVectorForDebug(
                template.OffsetX,
                template.OffsetY,
                template.MasterRobotAngle,
                out verifyWorldDx,
                out verifyWorldDy);

            double verifyPickX = template.RealRobotCenterX + verifyWorldDx;
            double verifyPickY = template.RealRobotCenterY + verifyWorldDy;
            double verifyPickRz = Models.Template.NormalizeAngle(
                template.MasterRobotAngle + template.OffsetRZ);

            _logger.Info(string.Format(
                "Offset Local Saved: OffsetX={0:F3}, OffsetY={1:F3}, OffsetLen={2:F3}, OffsetAngle={3:F3}, OffsetRZ={4:F3}",
                template.OffsetX,
                template.OffsetY,
                VectorLength(template.OffsetX, template.OffsetY),
                VectorAngleDeg(template.OffsetX, template.OffsetY),
                template.OffsetRZ));

            _logger.Info(string.Format(
                "Verify Setup Pick: PickX={0:F3}, PickY={1:F3}, RZ={2:F3}",
                verifyPickX,
                verifyPickY,
                verifyPickRz));

            _logger.Info(string.Format(
                "Verify Setup Error: ErrX={0:F6}, ErrY={1:F6}, ErrRZ={2:F6}",
                verifyPickX - _robotPose.X,
                verifyPickY - _robotPose.Y,
                Models.Template.NormalizeAngle(verifyPickRz - _robotPose.Rz)));
        }

        #endregion

        #region UI Helper

        private void LoadImageToViewer(Image<Bgr, byte> image)
        {
            if (image == null)
                return;

            BitmapSource source = Converter.BitmapToBitmapSource(image.Bitmap);
            imageViewer.LoadImage(source);
            imageViewer.Focus();
        }

        private void LoadBitmapToViewer(Bitmap bitmap)
        {
            if (bitmap == null)
                return;

            BitmapSource source = Converter.BitmapToBitmapSource(bitmap);
            imageViewer.LoadImage(source);
            imageViewer.Focus();
        }

        private void ShowError(string message)
        {
            ErrorWindow error = new ErrorWindow(message);
            error.ShowDialog();
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SetDoubleProperty(ref double field, double value, string propertyName)
        {
            if (Math.Abs(field - value) <= PropertyTolerance)
                return;

            field = value;
            OnPropertyChanged(propertyName);
        }

        private void SetBoolProperty(ref bool field, bool value, string propertyName)
        {
            if (field == value)
                return;

            field = value;
            OnPropertyChanged(propertyName);
        }

        #endregion
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

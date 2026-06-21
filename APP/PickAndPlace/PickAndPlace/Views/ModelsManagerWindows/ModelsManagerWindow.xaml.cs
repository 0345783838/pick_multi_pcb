using PickAndPlace.Controller.Robot;
using PickAndPlace.Controllers;
using PickAndPlace.Models;
using PickAndPlace.Views.UtilitiesWindows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PickAndPlace.Views.ModelsManagerWindows
{
    /// <summary>
    /// Interaction logic for ModelsManagerWindow.xaml
    /// </summary>
    public partial class ModelsManagerWindow : Window, INotifyPropertyChanged
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        Properties.Settings _param = Properties.Settings.Default;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ObservableCollection<ModelInfo> ModelsList { get; set; } = new ObservableCollection<ModelInfo>();

        private ModelInfo _selectedModel;
        public ModelInfo SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_selectedModel != value)
                {
                    _selectedModel = value;
                    CanSave = false;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanSave));
                    OnPropertyChanged(nameof(IsMoldelSelected));
                    OnPropertyChanged(nameof(RobotPoseString));
                }
            }
        }

        public bool CanSave { get; set; } = false;
        public bool IsMoldelSelected => SelectedModel != null;

        public string RobotPoseString => SelectedModel?.PickPose == null ? "-----" : $"{SelectedModel?.PickPose.X}, {SelectedModel?.PickPose.Y}, {SelectedModel?.PickPose.Z}, {SelectedModel?.PickPose.Rz}";


        private DobotRobotClient _robot;

        MainWindow _mainWindow;
        private Models.RobotPose _curPose;

        public ModelsManagerWindow(MainWindow window, string selectedModel)
        {
            InitializeComponent();
            _mainWindow = window;
            Init(selectedModel);
            DataContext = this;
        }

        private void Init(string selectedModel)
        {
            var modelList = ModelInfo.LoadModelsList();
            foreach (var item in modelList)
            {
                ModelsList.Add(item);
            }
            if (selectedModel == string.Empty)
            {
                SelectedModel = null;
            }
            else
            {
                SelectedModel = ModelsList.FirstOrDefault(x => x.Name == selectedModel);
            }
        }

        private void btReload_Click(object sender, RoutedEventArgs e)
        {
            var curModelName = SelectedModel.Name;
            ModelsList.Clear();
            var modelList = ModelInfo.LoadModelsList();
            foreach (var item in modelList)
            {
                ModelsList.Add(item);
            }
            SelectedModel = ModelsList.FirstOrDefault(x => x.Name == curModelName);
        }

        private void btAddModel_Click(object sender, RoutedEventArgs e)
        {
            var window = new AddModelNameWindow(this);
            window.ShowDialog();
        }

        private void btRemoveModel_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedModel != null)
            {
                var warning = new WarningWindow($"Are you sure to detele model {SelectedModel.Name}?\rBạn có muốn xóa model {SelectedModel.Name}?");
                var res = warning.ShowDialog();

                SelectedModel.Delete();
                btReload_Click(null, null);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (SelectedModel == null)
            {
                _mainWindow.Reload(string.Empty);
            }
            else
            {
                _mainWindow.Reload(SelectedModel.Name);
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9.]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void btnSave_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SelectedModel.SaveModel();
            btReload_Click(null, null);
            var info = new InformationWindow("Save successfully!\rLưu thành công!");
            info.ShowDialog();
        }

        internal void UpdateNewModelName(ModelInfo model)
        {
            ModelsList.Add(model);
            SelectedModel = model;
        }
  
        private void btnTemplateManager_Click(object sender, RoutedEventArgs e)
        {
            // Check if had robot coordinates, if not, show error and return.
            if (tbRobotX.Text == string.Empty || tbRobotY.Text == string.Empty || tbRobotZ.Text == string.Empty || tbRobotRz.Text == string.Empty)
            {
                var error = new ErrorWindow("Please get robot coordinates first!");
                error.ShowDialog();
                return;
            }
            try
            {
                double.TryParse(tbRobotX.Text, out double robotX);
                double.TryParse(tbRobotY.Text, out double robotY);
                double.TryParse(tbRobotZ.Text, out double robotZ);
                double.TryParse(tbRobotRz.Text, out double robotRz);

                _curPose = new Models.RobotPose(robotX, robotY, robotZ, robotRz);

                var window = new TemplatesSettingWindow(this, SelectedModel, _curPose);
                window.ShowDialog();
            }
            catch
            {
                var error = new ErrorWindow("Please get robot coordinates first!");
                error.ShowDialog();
                return;
            }
        }

        internal void UpdateModel(ModelInfo model)
        {
            SelectedModel = model;
            try
            {
                CanSave = true;
            }
            catch
            {
                CanSave = false;
                OnPropertyChanged(nameof(CanSave));
                return;
            }
            OnPropertyChanged(nameof(CanSave));
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

                if (!TryParseDobotPose(response, out double robotX, out double robotY, out double robotZ, out double robotRz))
                {
                    var error = new ErrorWindow($"Cannot parse robot pose!\rResponse: {response}");
                    error.ShowDialog();
                    return;
                }

                UpdateRobotCoord(robotX, robotY, robotZ, robotRz);
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex.Message, "SYSTEM");

                var error = new ErrorWindow($"Cannot get robot pose, err: {ex.Message}");
                error.ShowDialog();
            }
        }

        private void UpdateRobotCoord(double x, double y, double z, double rz)
        {
            this.Dispatcher.Invoke(() =>
            {
                tbRobotX.Text = x.ToString("0.00");
                tbRobotY.Text = y.ToString("0.00");
                tbRobotZ.Text = z.ToString("0.00");
                tbRobotRz.Text = rz.ToString("0.00");
            });

        }

        private static bool TryParseDobotPose(string response, out double x, out double y , out double z, out double rz)
        {
            x = 0;
            y = 0;
            z = 0;
            rz = 0;

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

            if (matches.Count < 4)
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

            bool okZ = double.TryParse(
                matches[2].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out z
            );

            bool okRz = double.TryParse(
                matches[3].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out rz
            );


            return okX && okY && okZ && okRz;
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

        private void tbRobot_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == null) return;

            string newText = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
                                    .Insert(tb.SelectionStart, e.Text);

            Regex regex = new Regex(@"^\d+(\.\d*)?$");

            e.Handled = !regex.IsMatch(newText);
        }
    }
}

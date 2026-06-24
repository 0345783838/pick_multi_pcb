using Microsoft.WindowsAPICodePack.Dialogs;
using PickAndPlace.Controllers.Camera;
using PickAndPlace.Models;
using PickAndPlace.Views.UtilitiesWindows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PickAndPlace.Views.SettingsWindows
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private Properties.Settings _param = Properties.Settings.Default;
        private CameraManager _cameraManager;

        public SettingsWindow()
        {
            InitializeComponent();
            Init();
            _cameraManager = CameraManager.GetInstance();
        }
        private void Init()
        {
            // Get cameras list
            List<CamInfo> camInfoList = LincolnCamera.GetListCamInfo();

            //
            for (int i = 0; i < camInfoList.Count; i++)
            {
                cbbCamSn.Items.Add(camInfoList[i].SN);
            }

            // Hardware Settings
            cbbCamSn.Text = _param.CamSn;
            tbCamExposure.Text = _param.CamExposure.ToString();
            tbRobotIp.Text = _param.RobotIp;
            tbRobotPort.Text = _param.RobotPort.ToString();

            // Saving Settings
            if (_param.SaveEnable == true)
            {
                cbSaveEnable.IsChecked = true;
                if (_param.SaveMode == (int)SaveType.ORIGINAL_RESULT)
                    rbSaveOptionResultOrigin.IsChecked = true;
                else if (_param.SaveMode == (int)SaveType.RESULT)
                    rbSaveOptionResult.IsChecked = true;
                else if (_param.SaveMode == (int)SaveType.ORIGINAL)
                    rbSaveOptionOrigin.IsChecked = true;
                tbSavePath.Text = _param.SavePath;
            }
            else
            {
                tbSavePath.Text = string.Empty;
                rbSaveOptionResultOrigin.IsChecked = false;
                rbSaveOptionResult.IsChecked = false;
                rbSaveOptionOrigin.IsChecked = false;
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void btnBrowser_Click(object sender, RoutedEventArgs e)
        {
            var savePaths = string.Empty;
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "Chọn thư mục lưu ảnh",
                Multiselect = false
            };
            WindowInteropHelper helper = new WindowInteropHelper(this);
            if (dialog.ShowDialog(helper.Handle) == CommonFileDialogResult.Ok)
            {
                savePaths = dialog.FileName;
                tbSavePath.Text = savePaths;
                tbSavePath.Focus();
                tbSavePath.CaretIndex = tbSavePath.Text.Length;
            }

            if (savePaths == string.Empty)
                return;
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (tbCamExposure.Text == string.Empty || int.Parse(tbCamExposure.Text) <= 0)
            {
                var error = new ErrorWindow("Please input exposure time!\rHãy nhập thời gian Exposure!");
                error.ShowDialog();
                return;
            }
            if (!IPAddress.TryParse(tbRobotIp.Text, out _))
            {
                var error = new ErrorWindow("Please input correct PLC IP!\rHãy nhập IP PLC chính xác!");
                error.ShowDialog();
                return;
            }
            if (cbbCamSn.Text == string.Empty)
            {
                var error = new ErrorWindow("Please select camera serial number!\rHãy chọn Serial Number cho camera!");
                error.ShowDialog();
                return;
            }
            // Check connection
            if (!_cameraManager.CheckCameraConnection(cbbCamSn.Text))
            {
                var error = new ErrorWindow($"Camera {cbbCamSn.Text} is not connected!\rKhông có kết nối camera {cbbCamSn.Text}!");
                error.ShowDialog();
                return;
            }

            //if (!PlcController.ConnectPlc(_param.ApiUrlCom, tbPlcIp.Text, int.Parse(tbPlcPort.Text)))
            //{
            //    var error = new ErrorWindow("No connection to PLC!\rKhông có kết nối PLC!");
            //    error.ShowDialog();
            //    return;
            //}
            //else
            //{
            //    PlcController.DisConnectPlc(_param.ApiUrlCom);
            //}
            // Save Settings
            _param.CamSn = cbbCamSn.Text;
            _param.CamExposure = int.Parse(tbCamExposure.Text);
            _param.RobotIp = tbRobotIp.Text;
            _param.RobotPort = int.Parse(tbRobotPort.Text);

            // Saving Settings
            if (cbSaveEnable.IsChecked == true)
            {
                if (tbSavePath.Text == string.Empty)
                {
                    var error = new ErrorWindow("Please select save path!\rHãy chọn thư mục lưu ảnh!");
                    error.ShowDialog();
                    return;
                }
                if (rbSaveOptionOrigin.IsChecked == false && rbSaveOptionResult.IsChecked == false && rbSaveOptionResultOrigin.IsChecked == false)
                {
                    var error = new ErrorWindow("Please select save option!\rHãy chọn mode lưu ảnh!");
                    error.ShowDialog();
                    return;
                }

                // Save Settings
                int saveMode = 1;
                if (rbSaveOptionOrigin.IsChecked == true)
                    saveMode = (int)SaveType.ORIGINAL;
                else if (rbSaveOptionResult.IsChecked == true)
                    saveMode = (int)SaveType.RESULT;
                else if (rbSaveOptionResultOrigin.IsChecked == true)
                    saveMode = (int)SaveType.ORIGINAL_RESULT;
                _param.SaveEnable = true;
                _param.SavePath = tbSavePath.Text;
                _param.SaveMode = saveMode;
            }
            else
            {
                _param.SaveEnable = false;
                _param.SavePath = string.Empty;
                _param.SaveMode = 1;
            }
            _param.Save();
            var info = new InformationWindow("Save Settings successfully!\rLưu Settings thành công!");
            info.ShowDialog();
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void btnCheckCamera_Click(object sender, RoutedEventArgs e)
        {
            if (cbbCamSn.Text == string.Empty)
            {
                var error = new ErrorWindow("Please select camera serial number!\rHãy chọn mã Serial cho camera!");
                error.ShowDialog();
                return;
            }

            var waiting = new WaitingWindow("Checking camera connection...\rĐang kiểm tra kết nối camera...");
            var cam1Sn = cbbCamSn.Text;
            bool resConnection = false;
            new Task(() =>
            {
                resConnection = _cameraManager.CheckCameraConnection(cam1Sn);
                waiting.KillMe = true;
            }).Start();

            waiting.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            waiting.ShowDialog();
            if (resConnection)
            {
                var info = new InformationWindow("Camera connection is OK!\rKết nối camera OK!");
                info.ShowDialog();
            }
            else
            {
                var error = new ErrorWindow("No camera connection!\rKhông có kết nối camera!");
                error.ShowDialog();
            }
        }

        private void btnCheckRobot_Click(object sender, RoutedEventArgs e)
        {
            if (!IPAddress.TryParse(tbRobotIp.Text, out _))
            {
                var error = new ErrorWindow("Invalid IP address!\rIP không hợp lệ!");
                error.ShowDialog();
                return;
            }
            if (tbRobotPort.Text == string.Empty)
            {
                var error = new ErrorWindow("Please enter the port number!\rHãy nhập số port!");
                error.ShowDialog();
                return;
            }

            var waiting = new WaitingWindow("Checking PLC connection...\rĐang kiểm tra kết nối PLC...");
            bool result = false;
            var plcIp = tbRobotIp.Text;
            var plcPort = int.Parse(tbRobotPort.Text);
            new Task(() =>
            {
                //result = PlcController.ConnectPlc(_param.ApiUrlCom, plcIp, plcPort);
                waiting.KillMe = true;
            }).Start();

            waiting.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            waiting.ShowDialog();

            if (result)
            {
                new Task(() =>
                {
                    //PlcController.DisConnectPlc(_param.ApiUrlCom);
                }).Start();
                var info = new InformationWindow("PLC connection is OK!\rKết nối PLC OK!");
                info.ShowDialog();
            }
            else
            {
                var error = new ErrorWindow("No PLC connection!\rKhông có kết nối PLC!");
                error.ShowDialog();
            }
        }

        private void tbPlcIp_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex(@"^-?(?:\d+)?(?:\.\d*)?$");
            e.Handled = !regex.IsMatch(e.Text);
        }

        private void tbRobotIp_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {

        }
    }
}

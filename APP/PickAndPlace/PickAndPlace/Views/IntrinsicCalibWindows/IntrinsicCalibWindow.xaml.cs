using PickAndPlace.Controllers.Camera;
using PickAndPlace.Views.UtilitiesWindows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace PickAndPlace.Views.IntrinsicCalibWindows
{
    /// <summary>
    /// Interaction logic for IntrinsicCalibWindow.xaml
    /// </summary>
    public partial class IntrinsicCalibWindow : Window
    {
        public IntrinsicCalibWindow()
        {
            InitializeComponent();
            Init();
        }

        private void Init()
        {
            List<CamInfo> camInfoList = LincolnCamera.GetListCamInfo();
            for (int i = 0; i < camInfoList.Count; i++)
            {
                cbbCamSn.Items.Add(camInfoList[i].SN);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void btnConnectCamera_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (cbbCamSn.SelectedItem == null)
            {
                var error = new ErrorWindow("Please choose a camera!\rHãy chọn camera!");
                error.ShowDialog();
                return;
            }

            //LincolnCamera.Connect(cbbCamSn.SelectedItem.ToString());
        }

        private bool CheckAndStartCamera()
        {
            //return true;
            //_cameraManager = CameraManager.GetInstance();
            //_camera1 = _cameraManager.GetCamera1();
            //_camera2 = _cameraManager.GetCamera2();
            //if (!_camera1.IsOpen())
            //{
            //    _mainWindow.ShowError(string.Format("Không mở được camera 1 với SN {0}\nCan't open 1 camera with SN:{0}", _param.Cam1Sn));
            //    return false;
            //}
            //if (!_camera2.IsOpen())
            //{
            //    _mainWindow.ShowError(string.Format("Không mở được camera 2 với SN {0}\nCan't open 2 camera with SN:{0}", _param.Cam2Sn));
            //    return true;
            //}
            //_camera1.SetExposureTime(_param.Cam1Exposure);
            //_camera2.SetExposureTime(_param.Cam2Exposure);
            //_camera1.Start();
            //_camera2.Start();
            return true;
        }

        private void btnCaptureImage_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void btnCalibrate_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void btnSave_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace PickAndPlace.Controllers.Camera
{
    internal class CameraManager
    {
        private static CameraManager _cameraManager;
        private LincolnCamera _camera;
        public static CameraManager GetInstance()
        {
            if (_cameraManager == null)
            {
                _cameraManager = new CameraManager();
            }

            return _cameraManager;
        }
        public static void Reload()
        {
            _cameraManager = new CameraManager();
        }
        public LincolnCamera GetCamera()
        {
            if (((_camera != null) && (_camera.SN != Properties.Settings.Default.CamSn)) || (_camera == null))
            {
                if (_camera != null)
                    _camera.Close();
                _camera = new LincolnCamera(Properties.Settings.Default.CamSn);
            }
            return _camera;
        }
        public LincolnCamera GetCamera(string sn)
        {
            if (((_camera != null) && (_camera.SN != sn)) || (_camera == null))
            {
                if (_camera != null)
                    _camera.Close();
                _camera = new LincolnCamera(sn);
            }
            return _camera;
        }

        public bool CheckCameraConnection(string SN)
        {
            var cam = GetCamera(SN);
            return cam.IsOpen();
        }
    }
}

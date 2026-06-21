using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace PickAndPlace.Models
{
    public class Template : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private int _id;

        public int Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged(nameof(Id));
                }
            }
        }

        [JsonIgnore]
        public Image<Bgr, byte> Image { get; set; }

        public string ImagePath { get; set; }

        // ROI master trên ảnh
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        // Góc ROI / object trên ảnh
        public double Angle { get; set; }

        // Góc object master sau khi đổi sang hệ robot
        public double MasterRobotAngle { get; set; }

        // Center master sau khi convert sang robot
        public double RealRobotCenterX { get; set; }
        public double RealRobotCenterY { get; set; }

        // Offset local từ center object tới điểm gắp
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }

        // Chênh lệch giữa RZ robot tại điểm gắp và góc object trong hệ robot
        public double OffsetRZ { get; set; }

        public Template()
        {
        }

        public Template(int id, string imagePath)
        {
            Id = id;
            ImagePath = imagePath;
            Image = new Image<Bgr, byte>(imagePath);
        }

        public Template(
            int id,
            Image<Bgr, byte> image,
            string imagePath,
            double centerX,
            double centerY,
            double width,
            double height,
            double angle)
        {
            Id = id;
            Image = image;
            ImagePath = imagePath;

            CenterX = centerX;
            CenterY = centerY;
            Width = width;
            Height = height;
            Angle = angle;
        }

        public static double NormalizeAngle(double angle)
        {
            while (angle > 180.0)
                angle -= 360.0;

            while (angle <= -180.0)
                angle += 360.0;

            return angle;
        }

        private static void RotateVector(
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

        public static double CalculateRobotAngleFromTwoRobotPoints(
            double centerRobotX,
            double centerRobotY,
            double directionRobotX,
            double directionRobotY)
        {
            double dx = directionRobotX - centerRobotX;
            double dy = directionRobotY - centerRobotY;

            if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
                throw new Exception("Cannot calculate robot angle because direction vector is zero.");

            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

            return NormalizeAngle(angle);
        }

        /// <summary>
        /// Setup master.
        /// 
        /// realRobotCenterX, realRobotCenterY:
        ///     Center ROI master sau khi convert pixel -> robot.
        /// 
        /// masterRobotAngle:
        ///     Góc object master trong hệ robot, tính từ 2 điểm robot.
        /// 
        /// robotX, robotY, robotRZ:
        ///     Pose robot tại điểm gắp chuẩn.
        /// </summary>
        public bool UpdateRealCoord(
            double realRobotCenterX,
            double realRobotCenterY,
            double masterRobotAngle,
            double robotX,
            double robotY,
            double robotRZ)
        {
            RealRobotCenterX = realRobotCenterX;
            RealRobotCenterY = realRobotCenterY;
            MasterRobotAngle = NormalizeAngle(masterRobotAngle);

            // Vector từ center object tới điểm gắp, trong hệ robot global
            double dxWorld = robotX - realRobotCenterX;
            double dyWorld = robotY - realRobotCenterY;

            // Đưa vector global về hệ local của object master
            RotateVector(
                dxWorld,
                dyWorld,
                -MasterRobotAngle,
                out double dxLocal,
                out double dyLocal
            );

            OffsetX = dxLocal;
            OffsetY = dyLocal;

            OffsetRZ = NormalizeAngle(robotRZ - MasterRobotAngle);

            return true;
        }
    }
}
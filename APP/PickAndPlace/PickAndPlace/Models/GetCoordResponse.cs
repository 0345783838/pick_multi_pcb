using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PickAndPlace.Models
{
    class GetCoordResponse
    {
        public bool Result { get; set; }
        public double? Score { get; set; }
        public double? ImageX { get; set; }
        public double? ImageY { get; set; }
        public double? ImageAngle { get; set; }
        public double? RobotX { get; set; }
        public double? RobotY { get; set; }
        public double? RobotAngle { get; set; }
        public string ResImg { get; set; }
        public string Message { get; set; }
    }
}

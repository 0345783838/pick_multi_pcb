using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PickAndPlace.Models
{
    public class RobotPose
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RZ { get; set; }

        public RobotPose() { }
        public RobotPose(double x, double y, double z, double rz) => (X, Y, Z, RZ) = (x, y, z, rz);
    }
}

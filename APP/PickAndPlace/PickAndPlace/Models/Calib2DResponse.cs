using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PickAndPlace.Models
{
    public class Calib2DResponse
    {
        public bool Result { get; set; }
        public double? RobotX { get; set; }
        public double? RobotY { get; set; }
        public string Message { get; set; }
    }
}

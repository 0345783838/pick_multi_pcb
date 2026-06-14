using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PickAndPlace.Models
{
    public class PairPoint : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

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
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public Tuple<double, double> ImagePixel { get; set; }
        public Tuple<double, double> RobotCoord { get; set; }

        public PairPoint(int id, Tuple<double, double> imagePixel, Tuple<double, double> robotCoord)
        {
            Id = id;
            ImagePixel = imagePixel;
            RobotCoord = robotCoord;
        }
    }
}

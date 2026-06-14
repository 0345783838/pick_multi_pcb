using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PickAndPlace.Models
{
    public class Template : INotifyPropertyChanged
    {
        Properties.Settings _param = Properties.Settings.Default;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
        public Template() { }


        public Template(int id, string imagePath)
        {
            Id = id;
            ImagePath = imagePath;
            Image = new Image<Bgr, byte>(imagePath);
        }
        public Template(int id, Image<Bgr, byte> image, string imagePath)
        {
            Id = id;
            Image = image;
            ImagePath = imagePath;
        }
    }
}

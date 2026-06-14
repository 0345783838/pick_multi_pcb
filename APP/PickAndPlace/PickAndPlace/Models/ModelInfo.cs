using Newtonsoft.Json;
using PickAndPlace.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI;

namespace PickAndPlace.Models
{
    public class ModelInfo: INotifyPropertyChanged
    {
        Properties.Settings _param = Properties.Settings.Default;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public string Name { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string CreatedTime { get; set; }
        private List<Template> _templates;
        public List<Template> Templates
        {
            get => _templates;
            set
            {
                if (_templates != value)
                {
                    _templates = value;
                    OnPropertyChanged(nameof(Templates));
                }
            }
        }


        public ModelInfo(string name, double width, double height, List<Template> templates, string createdTime) => (Name, Width, Height, Templates, CreatedTime) = (name, width, height, templates, createdTime);
        public ModelInfo() { }
        public ModelInfo(string name)
        {
            Name = name;
            CreatedTime = DateTime.Now.ToString();
            Templates = new List<Template>();
            Width = 0;
            Height = 0;
        }
        public static List<ModelInfo> LoadModelsList()
        {
            List<ModelInfo> modelNamesList = new List<ModelInfo>();
            IO.CreateFolderIfNotExists(Properties.Settings.Default.MODELS_PATH);
            string[] pathList = Directory.GetDirectories(Properties.Settings.Default.MODELS_PATH);
            for (int i = 0; i < pathList.Length; i++)
            {
                string name = Path.GetFileName(pathList[i]);
                var model = LoadModelByName(name);
                if (model != null)
                    modelNamesList.Add(model);
            }
            return modelNamesList;
        }
        public static ModelInfo LoadModelByName(string modelName)
        {
            ModelInfo model = null;
            string path = Properties.Settings.Default.MODELS_PATH + "/" + modelName + "/" + modelName + ".json";
            try
            {
                string str = File.ReadAllText(path);
                model = JsonConvert.DeserializeObject<ModelInfo>(str);
                if (model.Height == 0 || model.Width == 0 || model.Templates == null || model.Templates.Count == 0)
                {
                    // Remove invalid model
                    Directory.Delete(IO.GetFolderPath(path), true);
                    return null;
                }
            }
            catch (Exception ex)
            {
                return null;
            }
            return model;
        }
        public void SaveModel()
        {
            try
            {
                string modelPath = Properties.Settings.Default.MODELS_PATH + "/" + this.Name;
                IO.CreateFolderIfNotExists(modelPath);
                string json = JsonConvert.SerializeObject(this);
                File.WriteAllText(modelPath + "/" + this.Name + ".json", json);

                // Remove all old images in folder to save new ones
                string[] pathList = Directory.GetFiles(modelPath);
                for (int i = 0; i < pathList.Length; i++)
                {
                    if (!pathList[i].EndsWith(".json"))
                        File.Delete(pathList[i]);
                }

                // Save Images
                foreach (var template in this.Templates)
                {
                    template.Image.Save(template.ImagePath);
                }
            }
            catch (Exception ex)
            {
                return;
            }
        }
        public bool Delete(string modelName = null)
        {
            if (modelName == null)
                modelName = this.Name;
            string modelPath = Properties.Settings.Default.MODELS_PATH + "/" + modelName + "/" + modelName + ".json";
            try
            {
                File.Delete(modelPath);
                Directory.Delete(Properties.Settings.Default.MODELS_PATH + "/" + modelName, true);
            }
            catch
            {
                return false;
            }
            return true;
        }
    }
}

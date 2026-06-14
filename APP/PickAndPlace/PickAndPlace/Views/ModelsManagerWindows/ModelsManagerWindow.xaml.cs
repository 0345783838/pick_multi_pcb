using PickAndPlace.Models;
using PickAndPlace.Views.UtilitiesWindows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PickAndPlace.Views.ModelsManagerWindows
{
    /// <summary>
    /// Interaction logic for ModelsManagerWindow.xaml
    /// </summary>
    public partial class ModelsManagerWindow : Window, INotifyPropertyChanged
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        Properties.Settings _param = Properties.Settings.Default;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ObservableCollection<ModelInfo> ModelsList { get; set; } = new ObservableCollection<ModelInfo>();

        private ModelInfo _selectedModel;
        public ModelInfo SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_selectedModel != value)
                {
                    _selectedModel = value;
                    CanSave = false;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanSave));
                    OnPropertyChanged(nameof(IsMoldelSelected));
                }
            }
        }

        public bool CanSave { get; set; } = false;
        public bool IsMoldelSelected => SelectedModel != null;

        MainWindow _mainWindow;
        public ModelsManagerWindow(MainWindow window, string selectedModel)
        {
            InitializeComponent();
            _mainWindow = window;
            Init(selectedModel);
            DataContext = this;
        }

        private void Init(string selectedModel)
        {
            var modelList = ModelInfo.LoadModelsList();
            foreach (var item in modelList)
            {
                ModelsList.Add(item);
            }
            if (selectedModel == string.Empty)
            {
                SelectedModel = null;
            }
            else
            {
                SelectedModel = ModelsList.FirstOrDefault(x => x.Name == selectedModel);
            }
        }

        private void btReload_Click(object sender, RoutedEventArgs e)
        {
            var curModelName = SelectedModel.Name;
            ModelsList.Clear();
            var modelList = ModelInfo.LoadModelsList();
            foreach (var item in modelList)
            {
                ModelsList.Add(item);
            }
            SelectedModel = ModelsList.FirstOrDefault(x => x.Name == curModelName);
        }

        private void btAddModel_Click(object sender, RoutedEventArgs e)
        {
            var window = new AddModelNameWindow(this);
            window.ShowDialog();
        }

        private void btRemoveModel_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedModel != null)
            {
                var warning = new WarningWindow($"Are you sure to detele model {SelectedModel.Name}?\rBạn có muốn xóa model {SelectedModel.Name}?");
                var res = warning.ShowDialog();

                SelectedModel.Delete();
                btReload_Click(null, null);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (SelectedModel == null)
            {
                _mainWindow.Reload(string.Empty);
            }
            else
            {
                _mainWindow.Reload(SelectedModel.Name);
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9.]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void btnSave_MouseDown(object sender, MouseButtonEventArgs e)
        {
            int w;
            int h;
            try
            {
                w = Convert.ToInt32(tbImageWidth.Text);
                h = Convert.ToInt32(tbPcbHeight.Text);
            }
            catch
            {
                var box = new ErrorWindow("Width and Height are not valid!\rChiều rộng và chiều cao PCB không hợp lệ!");
                box.ShowDialog();
                return;
            }

            if (w == 0 || h == 0)
            {
                var box = new ErrorWindow("Width and Height must be greater than 0!\rChiều rộng và chiều cao PCB phải lớn hơn 0!");
                box.ShowDialog();
                return;
            }
            SelectedModel.Height = Convert.ToInt32(tbPcbHeight.Text);
            SelectedModel.Width = Convert.ToInt32(tbImageWidth.Text);
            SelectedModel.SaveModel();
            btReload_Click(null, null);
            var info = new InformationWindow("Save successfully!\rLưu thành công!");
            info.ShowDialog();
        }

        internal void UpdateNewModelName(ModelInfo model)
        {
            ModelsList.Add(model);
            SelectedModel = model;
        }

        private void tbImageWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SelectedModel == null)
            {
                return;
            }
            TextBox tb = sender as TextBox;
            string text = tb.Text;
            int x = 0;
            int y = 0;
            try
            {
                x = Convert.ToInt32(text);
                y = Convert.ToInt32(tbPcbHeight.Text);
            }
            catch
            {
                return;
            }


            if (x != SelectedModel.Width && y != 0 && x != 0)
            {
                CanSave = true;
                OnPropertyChanged(nameof(CanSave));
            }
            else
            {
                CanSave = false;
                OnPropertyChanged(nameof(CanSave));
            }
        }

        private void tbPcbHeight_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SelectedModel == null)
            {
                return;
            }
            TextBox tb = sender as TextBox;
            string text = tb.Text;
            int x = 0;
            int y = 0;
            try
            {
                y = Convert.ToInt32(text);
                x = Convert.ToInt32(tbImageWidth.Text);
            }
            catch
            {
                return;
            }

            if (y != SelectedModel.Height && x != 0 && y != 0)
            {
                CanSave = true;
                OnPropertyChanged(nameof(CanSave));
            }
            else
            {
                CanSave = false;
                OnPropertyChanged(nameof(CanSave));
            }
        }

        private void btnTemplateManager_Click(object sender, RoutedEventArgs e)
        {
            var window = new TemplatesSettingWindow(this, SelectedModel);
            window.ShowDialog();
        }

        internal void UpdateModel(ModelInfo model)
        {
            SelectedModel = model;
            int w;
            int h;
            try
            {
                w = Convert.ToInt32(tbImageWidth.Text);
                h = Convert.ToInt32(tbPcbHeight.Text);
            }
            catch
            {
                CanSave = false;
                OnPropertyChanged(nameof(CanSave));
                return;
            }
            if (w > 0 && h > 0) CanSave = true;
            else CanSave = false;
            OnPropertyChanged(nameof(CanSave));
        }
    }
}

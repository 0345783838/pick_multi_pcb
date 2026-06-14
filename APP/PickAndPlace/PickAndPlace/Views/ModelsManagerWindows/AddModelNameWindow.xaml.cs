using PickAndPlace.Models;
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
using System.Windows.Shapes;

namespace PickAndPlace.Views.ModelsManagerWindows
{
    /// <summary>
    /// Interaction logic for AddModelNameWindow.xaml
    /// </summary>
    public partial class AddModelNameWindow : Window
    {
        ModelsManagerWindow _modelManagerWindow;
        public AddModelNameWindow(ModelsManagerWindow window)
        {
            InitializeComponent();
            _modelManagerWindow = window;
            tbModelName.Focus();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            string modelName = tbModelName.Text;
            if (modelName == string.Empty)
            {
                var box = new ErrorWindow("Model name cannot be empty!\rTên model không được để trống!");
                box.Topmost = true;
                box.ShowDialog();
            }
            else
            {
                var items = _modelManagerWindow.ModelsList;
                if (items == null || !items.Select(x => x.Name).Contains(modelName))
                {
                    ModelInfo model = new ModelInfo(modelName);
                    model.SaveModel();
                    _modelManagerWindow.UpdateNewModelName(model);
                    this.Close();
                }
                else
                {
                    var box = new ErrorWindow("Model Name is existed!\rTên model đã tồn tại!");
                    box.Topmost = true;
                    box.ShowDialog();
                }
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Button_Click(this, null);
            }
        }
    }
}

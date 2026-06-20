using LiveCharts;
using LiveCharts.Wpf;
using PickAndPlace.Controller;
using PickAndPlace.Controllers;
using PickAndPlace.Models;
using PickAndPlace.Services;
using PickAndPlace.Utils;
using PickAndPlace.Views.EyeHand2dCalibWindows;
using PickAndPlace.Views.ModelsManagerWindows;
using PickAndPlace.Views.SettingsWindows;
using PickAndPlace.Views.UtilitiesWindows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PickAndPlace.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private Properties.Settings _param = Properties.Settings.Default;
        public event PropertyChangedEventHandler PropertyChanged;

        public AppLogger Logger => AppLogger.Instance;
        public SeriesCollection PieSeriesCollection { get; set; }
        private PieSeries _okSeries;
        private PieSeries _ngSeries;
        public int InspectionStatus { get; set; } = (int)(StatusState.Unknown);

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private MainController _mainController;

        public int AiStatus { get; set; } = (int)(StatusState.Unknown);

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
                    OnPropertyChanged();
                }
            }
        }


        public MainWindow()
        {
            InitializeComponent();
            _mainController = new MainController(this);
            DataContext = this;
            Init();
            InitStatistics();
            Logger.Logs.CollectionChanged += Logs_CollectionChanged;
        }

        private void Init()
        {
            var modelsList = ModelInfo.LoadModelsList();
            foreach (var item in modelsList)
            {
                ModelsList.Add(item);
            }
        }

        private void Logs_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action !=
                System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                return;

            if (LogListBox.Items.Count == 0)
                return;

            // Scroll sau khi UI render xong
            LogListBox.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    LogListBox.ScrollIntoView(
                        LogListBox.Items[LogListBox.Items.Count - 1]);
                }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _mainController.Close();
        }
        private void DisableWindows()
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                btnSettings.IsEnabled = false;
                btnModelsManager.IsEnabled = false;
                btnCalibEyeToHand2D.IsEnabled = false;
                btnStart.IsEnabled = false;
                btnStop.IsEnabled = false;
                btnTest.IsEnabled = false;
            }));
        }
        private void EnableWindows()
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                btnSettings.IsEnabled = true;
                btnModelsManager.IsEnabled = true;
                btnCalibEyeToHand2D.IsEnabled = true;
                btnStart.IsEnabled = true;
            }));
   
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            DisableWindows();
            var waiting = new WaitingWindow("Checking machine license...");
            waiting.Topmost = false;
            bool resLicense = false;
            new Task(() =>
            {
                resLicense = _mainController.CheckLicense();
                waiting.KillMe = true;
            }).Start();
            waiting.ShowDialog();
           
            if (resLicense)
            {
                bool res = false;
                res = await _mainController.RunServiceAsync(20000, "Loading program...");
         
            }
            else
            {
                AppLogger.Instance.Error("Machine license is not valid! Please contact vendor!", "SYSTEM");
            }
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow();
            window.ShowDialog();
        }   

        private void btnCalibIntrinsic_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnCalibEyeToHand2D_Click(object sender, RoutedEventArgs e)
        {
            var window = new EyeHand2dCalibWindow();
            window.ShowDialog();
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedModel == null)
            {
                var box = new ErrorWindow("Please select a running model!\rVui lòng chọn model chạy!");
                box.ShowDialog();
                return;
            }

            bool startOK = false;
            WaitingWindow wait = null;

            try
            {
                // Disable trước để tránh user bấm nhiều lần
                SetRunningControlsEnabled(false);

                wait = new WaitingWindow("Checking running conditions...\rKiểm tra điều kiện chạy");
                wait.Owner = this;
                wait.Show();

                // KHÔNG dùng new Task().Start()
                // KHÔNG dùng Task.Run nếu trong StartAsync có gọi UI như ShowError, ShowWarning
                startOK = await _mainController.StartAsync(SelectedModel);
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex.Message, "START_FAILED");
                startOK = false;
            }
            finally
            {
                if (wait != null)
                {
                    wait.KillMe = true;
                    wait.Close();
                }

                ApplyRunStateToUi(startOK);
            }
        
        }
        private void SetRunningControlsEnabled(bool enabled)
        {
            btnStart.IsEnabled = enabled;
            cbbModelNames.IsEnabled = enabled;
            btnSettings.IsEnabled = enabled;
            btnModelsManager.IsEnabled = enabled;
            btnCalibEyeToHand2D.IsEnabled = enabled;

            btnStop.IsEnabled = !enabled;
            btnTest.IsEnabled = !enabled;
        }

        private void ApplyRunStateToUi(bool isRunning)
        {
            btnStart.IsEnabled = !isRunning;
            cbbModelNames.IsEnabled = !isRunning;
            btnSettings.IsEnabled = !isRunning;
            btnModelsManager.IsEnabled = !isRunning;
            btnCalibEyeToHand2D.IsEnabled = !isRunning;

            btnStop.IsEnabled = isRunning;
            btnTest.IsEnabled = isRunning;
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            _mainController.Stop();
            btnStart.IsEnabled = true;
            cbbModelNames.IsEnabled = true;
            btnSettings.IsEnabled = true;
            btnModelsManager.IsEnabled = true;
            btnCalibEyeToHand2D.IsEnabled = true;
            btnStop.IsEnabled = false;
            btnTest.IsEnabled = false;
        }

        internal void SetLoadingService(string content)
        {
            var timeout = 10000;
            new Thread(() =>
            {
                this.Dispatcher.Invoke(new Action(() =>
                {
                    WaitingWindow wait = new WaitingWindow(content);
                    new Task(() =>
                    {
                        var timestep = timeout / 500;
                        for (int i = 0; i < timestep; i++)
                        {
                            Thread.Sleep(500);
                            if (_mainController._serviceIsRun)
                            {
                                break;
                            }
                        }
                        wait.KillMe = true;
                        if (!_mainController._serviceIsRun)
                        {
                            this.Dispatcher.Invoke(new Action(() =>
                            {
                                UpdateAIStatus(false);
                                var box = new ErrorWindow("Cannot start AI service! Please contact IT!\rKhông khởi động được AI, Hãy liên hệ bộ phận PI");
                                box.ShowDialog();
                            }));
                        }
                        else
                        {
                            UpdateAIStatus(true);
                            EnableWindows();
                        }
                    }).Start();
                    wait.ShowDialog();
                }));
            }).Start();
        }
        private void UpdateAIStatus(bool resAI)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                AiStatus = resAI ? (int)(StatusState.Ok) : (int)(StatusState.Ng);
                OnPropertyChanged(nameof(AiStatus));
            }));
        }
     
        public bool ShowWarning(string content)
        {
            var res = false;
            this.Dispatcher.Invoke(new Action(() =>
            {
                var box = new WarningWindow(content);
                box.ShowDialog();
                res = (bool)box.DialogResult;
            }));
            return res;
        }
        public void ShowError(string content)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                ErrorService.ShowError(content);
            }));
        }
        public void ShowInfo(string content)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                var box = new InformationWindow(content);
                box.ShowDialog();
            }));
        }

        private async void btnTest_Click(object sender, RoutedEventArgs e)
        {
            await _mainController.ProcessImageAsync(SelectedModel);
        }
        public void UpdateImage(System.Drawing.Bitmap image)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                if (image == null)
                {
                    imbImage.ImageSource = null;
                    imbImage.ClearShapes();
                }
                else if (imbImage.ImageSource == null)
                {

                    BitmapSource source = Converter.BitmapToBitmapSource(image);
                    imbImage.LoadImage(source);
                    imbImage.FitToScreen();
                }
                else
                {
                    BitmapSource source = Converter.BitmapToBitmapSource(image);
                    imbImage.LoadImage(source);
                }
            }));
        }
        internal void UpdateCurrentShiftTime(string curShiftTime)
        {
            DateTime dt = DateTime.ParseExact(
                curShiftTime,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                lbDate.Content = dt.ToString("dd/MM/yyyy");
                lbWorkingShift.Content = $"{dt.ToString("HH:mm")} - {dt.AddHours(12).ToString("HH:mm")}";
            }));
        }
        internal void UpdateStatistics(bool status, bool firstTime = false)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Check shift time
                var curShiftTime = MyDateTime.GetCurShiftStartTime();
                if (curShiftTime != _param.StartShiftTime)
                {
                    _param.StartShiftTime = curShiftTime;
                    _param.CurrentOK = 0;
                    _param.CurrentNG = 0;
                    _param.Save();
                    UpdateCurrentShiftTime(_param.StartShiftTime);
                }

                if (firstTime)
                {
                    tbOKCount.Text = _param.CurrentOK.ToString();
                    tbNGCount.Text = _param.CurrentNG.ToString();
                    UpdateStatistics(_param.CurrentOK, _param.CurrentNG);
                    UpdateCurrentShiftTime(_param.StartShiftTime);
                }
                else
                {
                    if (status)
                    {
                        _param.CurrentOK += 1;
                        tbOKCount.Text = _param.CurrentOK.ToString();
                    }
                    else
                    {
                        _param.CurrentNG += 1;
                        tbNGCount.Text = _param.CurrentNG.ToString();
                    }
                    UpdateStatistics(_param.CurrentOK, _param.CurrentNG);
                    _param.Save();
                }
            }));
        }
        private void InitStatistics()
        {
            _okSeries = new PieSeries
            {
                Title = "OK",
                Values = new ChartValues<double> { 0 },
                DataLabels = true,
                LabelPoint = chartPoint => $"{chartPoint.Participation:P2}", // <-- thêm %
                Fill = new SolidColorBrush(System.Windows.Media.Colors.Green)
            };

            _ngSeries = new PieSeries
            {
                Title = "Failed",
                Values = new ChartValues<double> { 0 },
                DataLabels = true,
                LabelPoint = chartPoint => $"{chartPoint.Participation:P2}",
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 68, 73))
            };
            PieSeriesCollection = new SeriesCollection { _okSeries, _ngSeries };
            UpdateStatistics(true, firstTime: true);
        }
        public void UpdateStatistics(int okCount, int ngCount)
        {
            this.Dispatcher.Invoke(() =>
            {
                _okSeries.Values[0] = (double)okCount;
                _ngSeries.Values[0] = (double)ngCount;
            });
        }

        public void UpdateCalculateResult(double score, double imageX, double imageY, double imageAngle, double robotX, double robotY, double robotW)
        {
            this.Dispatcher.Invoke(() =>
            {
                lbScore.Content = score.ToString("0.0000");
                lbImageXYR.Content = $"{imageX.ToString("0.0")},  {imageY.ToString("0.0")},  {imageAngle.ToString("0.0")}";
                lbRobotXYW.Content = $"{robotX.ToString("0.0")},  {robotY.ToString("0.0")},  {robotW.ToString("0.0")}";
            });
        }
        internal void UpdateInspectionStatus(bool status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                InspectionStatus = status ? (int)(StatusState.Ok) : (int)(StatusState.Ng);
                OnPropertyChanged(nameof(InspectionStatus));
            }));
        }

        private void btnModelsManager_Click(object sender, RoutedEventArgs e)
        {
            ModelsManagerWindow window;
            if (SelectedModel == null)
                window = new ModelsManagerWindow(this, string.Empty);
            else
                window = new ModelsManagerWindow(this, SelectedModel.Name);
            window.ShowDialog();
        }

        internal void Reload(string name)
        {
            ModelsList.Clear();
            var newList = ModelInfo.LoadModelsList();
            foreach (var item in newList)
            {
                ModelsList.Add(item);
            }
            SelectedModel = ModelsList.FirstOrDefault(x => x.Name == name);
        }
    }
}

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
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO.Ports;
using System.IO;
using System.ComponentModel;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Xaml.Behaviors;

namespace BellManager
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }

    public class AutoSelectBehavior : Behavior<System.Windows.Controls.TextBox>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.Loaded += ItemLoaded;
            AssociatedObject.Unloaded += ItemUnloaded;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            AssociatedObject.Loaded -= ItemLoaded;
            ItemUnloaded(AssociatedObject, new RoutedEventArgs());
            AssociatedObject.Unloaded -= ItemUnloaded;
        }
        protected override void OnChanged()
        {
            base.OnChanged();
        }

        private void ItemLoaded(object sender, RoutedEventArgs args)
        {
            AssociatedObject.GotFocus += (obj, e) =>
            {
                AssociatedObject.SelectAll();
            };
        }
        private void ItemUnloaded(object sender, RoutedEventArgs args)
        {
            AssociatedObject.GotFocus -= (obj, e) =>
            {
                AssociatedObject.SelectAll();
            };
        }
    }
    
    public class JsonAppConfig
    {
        public int? PortBaudRate { get; set; }
        public int? PortStopBits { get; set; }
        public int? PortDataBits { get; set; }
        public string Prefix { get; set; }
        public string Postfix { get; set; }
        public string Splitter { get; set; }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly string prefix = "$GPSDL,";
        private readonly string postfix = "*";
        private readonly string splitter = ",";


        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string property = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
        private string name = "Безымянный";
        private string selectedPortName;
        public string Name
        {
            get { return name; }
            set
            {
                name = value;
                OnPropertyChanged("Name");
            }
        }

        public MainViewModel() : base()
        {
            Ports = new ObservableCollection<string>();
            foreach(var p in PortManager.PortNames)
            {
                Ports.Add(p);
            }
            PortManager.PortConnected += (args) =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var p in args.NewPorts)
                    {
                        Ports.Add(p);
                    }
                });
            };
            PortManager.PortDisconnected += (args) =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var p in args.DeletedPorts)
                    {
                        if (Ports.Contains(p))
                            Ports.Remove(p);
                    }
                });
            };
            if(File.Exists("config.json"))
            {
                var conf = JsonConvert.DeserializeObject<JsonAppConfig>(File.ReadAllText("config.json"));
                prefix = conf.Prefix ?? prefix;
                postfix = conf.Postfix ?? postfix;
                splitter = conf.Splitter ?? splitter;
                PortManager.BaudRate = conf.PortBaudRate ?? PortManager.BaudRate;
                PortManager.DataBits = conf.PortDataBits ?? PortManager.DataBits;
                PortManager.StopBits = (StopBits)(conf.PortStopBits ?? (int)PortManager.StopBits);
            }
            else
            {
                File.WriteAllText("config.json", JsonConvert.SerializeObject(new JsonAppConfig()
                {
                    Prefix = prefix,
                    Postfix = postfix,
                    Splitter = splitter,
                    PortBaudRate = PortManager.BaudRate,
                    PortDataBits = PortManager.DataBits,
                    PortStopBits = (int?)PortManager.StopBits,
                }));
            }
        }

        public string SelectedPortName
        {
            get { return selectedPortName; }
            set
            {
                selectedPortName = value;
                OnPropertyChanged("SelectedPortName");
            }
        }

        public ObservableCollection<string> Ports
        {
            get; set;
        }

        public ObservableCollection<Bell> Bells { get; set; } = new ObservableCollection<Bell>()
        {
            new Bell(),
            new Bell(),
            new Bell(),
            new Bell(),
            new Bell(),
            new Bell(),
            new Bell(),
            new Bell(),
            new Bell(),
            new Bell(),
        };

        private JsonConfig openJsonConfig(string path)
        {
            if(string.IsNullOrEmpty(path)) return null;
            if(!File.Exists(path)) return null;
            var config = JsonConvert.DeserializeObject<JsonConfig>(File.ReadAllText(path));
            return config;
        }

        private void loadJsonConfig(JsonConfig config)
        {
            if(config == null) return;
            if((config.Bells?.Length ?? 0) > 0)
            {
                Bells.Clear();
                foreach(var bell in config.Bells)
                {
                    Bells.Add(bell);
                }
            }
            if(!string.IsNullOrEmpty(config.Name))
            {
                Name = config.Name;
            }
            else
            {
                Name = "Безымянный";
            }
        }
        
        public void OpenSavedConfig(string pathOfConfigFile)
        {
            loadJsonConfig(openJsonConfig(pathOfConfigFile));
        }

        private RelayCommand saveCommand;
        private RelayCommand connectAndSendCommand;
        private RelayCommand openCommand;

        public RelayCommand SaveCommand
        {
            get
            {
                return saveCommand ?? (saveCommand = new RelayCommand(() =>
                {
                    JsonConfig config = new JsonConfig()
                    {
                        Name = this.Name,
                        Bells = this.Bells.ToArray(),
                    };
                    SaveFileDialog fileDialog = new SaveFileDialog()
                    {
                        AddExtension = true,
                        DefaultExt = ".json",
                        Filter = "JSON Files|*.json",
                        OverwritePrompt = true,
                    };
                    bool result = fileDialog.ShowDialog() == DialogResult.OK;
                    if (result)
                    {
                        File.WriteAllText(fileDialog.FileName, JsonConvert.SerializeObject(config));
                    }
                }));
            }
        }

        public RelayCommand ConnectAndSendCommand
        {
            get
            {
                return connectAndSendCommand ?? (connectAndSendCommand = new RelayCommand(() =>
                {
                    bool res = PortManager.SendDataToPort(SelectedPortName, createDataStringForCOM());
                    if(res)
                    {
                        System.Windows.MessageBox.Show($"Данные переданы в порт {SelectedPortName}", "Успех", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"Ошибка передачи в порт {SelectedPortName}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }));
            }
        }

        private string createDataStringForCOM()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(prefix);
            foreach(var bell in this.Bells)
            {
                builder.Append(splitter);
                builder.Append(bell.BellNumber > 10 ? "" : "0" + bell.BellNumber);
                builder.Append(bell.IsEnabled ? "1" : "0");
                builder.Append(bell.StartTimeH);
                builder.Append(bell.StartTimeM);
                builder.Append(bell.EndTimeH);
                builder.Append(bell.EndTimeM);
            }
            builder.Append(postfix);
            return builder.ToString();
        }

        public RelayCommand OpenCommand
        {
            get
            {
                return openCommand ?? (openCommand = new RelayCommand(() =>
                {
                    OpenFileDialog openFileDialog = new OpenFileDialog()
                    {
                        Filter = "JSON Files|*.json",
                        Title = "Открыть конфигурацию",
                        CheckFileExists = true,
                        CheckPathExists = true,
                        Multiselect = false,
                    };
                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        OpenSavedConfig(openFileDialog.FileName);
                    }
                }));
            }
        }        
    }

    public class RelayCommand : ICommand
    {
        private Action _execute;
        private Func<bool> _canExecute;
        
        public RelayCommand(Action _execute, Func<bool> _canExecute = null)
        {
            this._execute = _execute;
            this._canExecute = _canExecute;
        }

        public void Execute(object obj)
        {
            _execute?.Invoke();
        }
        public bool CanExecute(object obj)
        {
            return _canExecute?.Invoke() ?? true;
        }
        public event EventHandler CanExecuteChanged
        {
            add
            {
                CommandManager.RequerySuggested += value;
            }
            remove
            {
                CommandManager.RequerySuggested -= value;
            }
        }
    }

    public class Bell : INotifyPropertyChanged
    {
        private static int counter = 1;
        private static int startHours = 8;
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string property = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        private int startTimeH = startHours++;
        private int startTimeM = 30;
        private int endTimeH = startHours;
        private int endTimeM = 15;
        private bool isEnabled = true;
        private int bellNumber = counter++;
        public int StartTimeH
        {
            get { return startTimeH; }
            set
            {
                startTimeH = value > 23 ? 23 : (value < 0 ? 0 : value);
                OnPropertyChanged("StartTimeH");
            }
        }
        public int StartTimeM
        {
            get { return startTimeM; }
            set
            {
                startTimeM = value > 59 ? 59 : (value < 0 ? 0 : value);
                OnPropertyChanged("StartTimeM");
            }
        }

        public int EndTimeH
        {
            get { return endTimeH; }
            set
            {
                endTimeH = value > 23 ? 23 : (value < 0 ? 0 : value);
                OnPropertyChanged("EndTimeH");
            }
        }
        public int EndTimeM
        {
            get { return endTimeM; }
            set
            {
                endTimeM = value > 59 ? 59 : (value < 0 ? 0 : value);
                OnPropertyChanged("EndTimeM");
            }
        }
        public bool IsEnabled
        {
            get { return isEnabled; }
            set
            {
                isEnabled = value;
                OnPropertyChanged("IsEnabled");
            }
        }
        public int BellNumber
        {
            get { return bellNumber; }
            set
            {
                bellNumber = value;
                OnPropertyChanged("BellNumver");
            }
        }
    }

    public class JsonConfig
    {
        public string Name;
        public Bell[] Bells;
    }
    public class SerialPortEventArgs
    {
        public string[] Ports;
        public string[] NewPorts;
        public string[] DeletedPorts;
    }
    public delegate void SerialPortEvent(SerialPortEventArgs args);



    public static class PortManager
    {
        private static Thread serialPortChecker;
        public static event SerialPortEvent PortConnected;
        public static event SerialPortEvent PortDisconnected;

        public static int BaudRate { get; set; } = 9600;
        public static StopBits StopBits { get; set; } = StopBits.One;
        public static int DataBits { get; set; } = 8;

        static PortManager()
        {
            serialPortChecker =
                new Thread(() =>
                {
                    while(true)
                    {
                        string[] newPortNames = SerialPort.GetPortNames();
                        if(PortNames != newPortNames)
                        {
                            if(PortNames.Except(newPortNames).Count() > 0)
                            {
                                PortDisconnected?.Invoke(new SerialPortEventArgs()
                                {
                                    Ports = newPortNames,
                                    NewPorts = new string[0],
                                    DeletedPorts = PortNames.Except(newPortNames).ToArray(),
                                });
                            }
                            if(newPortNames.Except(PortNames).Count() > 0)
                            {
                                PortConnected?.Invoke(new SerialPortEventArgs()
                                {
                                    Ports = newPortNames,
                                    DeletedPorts = new string[0],
                                    NewPorts = newPortNames.Except(PortNames).ToArray(),
                                });
                            }
                            PortNames = newPortNames;
                        }
                    }
                })
                {
                    Name = "Serial Ports Checker",
                    IsBackground = true,
                };
            serialPortChecker.Start();

        }

        public static string[] PortNames = SerialPort.GetPortNames();

        private static bool sendDataStringToPort(SerialPort port, string data)
        {
            if (port == null) return false;
            if (string.IsNullOrEmpty(data)) return false;
            try
            {
                port.Write(data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool openPort(string portName, ref SerialPort port)
        {
            if (string.IsNullOrEmpty(portName)) return false;
            port = new SerialPort(portName);
            port.BaudRate = BaudRate;
            port.Parity = Parity.None;
            port.StopBits = StopBits;
            port.DataBits = DataBits;
            port.ReadTimeout = 1500;
            port.WriteTimeout = 1500;
            try
            {
                port.Open();
                return port.IsOpen;
            }
            catch
            {
                return false;
            }
        }
        private static void closePort(ref SerialPort port)
        {
            if (port?.IsOpen ?? false) port.Close();
        }

        public static bool SendDataToPort(string portName, string data)
        {
            if (string.IsNullOrEmpty(portName) || string.IsNullOrEmpty(data)) return false;
            SerialPort port = null;
            if(!openPort(portName, ref port))
            {
                closePort(ref port);
                return false;
            }
            if(!sendDataStringToPort(port, data))
            {
                closePort(ref port);
                return false;
            }
            closePort(ref port);
            return true;
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Drawing;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Forms.ContextMenu;
using System.Windows.Media;
using System.Windows.Input;
using System.Diagnostics;
using System.IO;

namespace FloatingNote
{
    public partial class MainWindow : Window
    {
        private NotifyIcon _notifyIcon;
        private FloatingNoteWindow _noteWindow;
        private int _currentGradientIndex = 0;

        private Process _pythonProcess;

        public ObservableCollection<ReminderItem> Reminders { get; set; } = new ObservableCollection<ReminderItem>();
        private ReminderItem _editingItem = null;

        public MainWindow()
        {
            InitializeComponent();

            if (Reminders.Count == 0)
            {
                Reminders.Add(new ReminderItem { Message = "Welcome to your new dashboard! ✨", DurationSeconds = 5 });
                Reminders.Add(new ReminderItem { Message = "Add your own messages below 👇", DurationSeconds = 8 });
            }

            RemindersList.ItemsSource = Reminders;
            FontSizeInput.Text = "60";
        }

        // --- PYTHON PROCESS MANAGEMENT ---

        private void StartPythonBackend()
        {
            try
            {
                string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "now_playing.py");

                if (!File.Exists(scriptPath))
                {
                    ShowError($"Could not find 'now_playing.py' at:\n{scriptPath}\nPlease move the file there.");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,

                    // IMPORTANT: Set this to FALSE so the window appears initially.
                    // The Python script will hide itself after auth or timeout.
                    CreateNoWindow = false,

                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                _pythonProcess = Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ShowError($"Failed to start Sync Service: {ex.Message}");
            }
        }

        private void StopPythonBackend()
        {
            try
            {
                if (_pythonProcess != null && !_pythonProcess.HasExited)
                {
                    _pythonProcess.Kill();
                    _pythonProcess = null;
                }
            }
            catch { }
        }

        // --- APP LIFECYCLE ---

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CreateTrayIcon();
            StartPythonBackend();
        }

        private void OnExitApplication(object sender, EventArgs e)
        {
            _notifyIcon?.Dispose();
            _noteWindow?.Close();
            StopPythonBackend();
            Application.Current.Shutdown();
        }

        // --- REST OF CODE ---
        private void GradientButton_Click(object sender, RoutedEventArgs e)
        {
            _currentGradientIndex = (_currentGradientIndex + 1) % GradientPresets.SpotifyLikeGradients.Count;
            this.Resources["DashboardBackgroundBrush"] = GradientPresets.SpotifyLikeGradients[_currentGradientIndex];
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            bool isSpotify = SpotifyCheckBox.IsChecked == true;

            if (!isSpotify && Reminders.Count == 0)
            {
                ShowError("Please add at least one message.");
                return;
            }

            if (!double.TryParse(FontSizeInput.Text, out double fontSize) || fontSize <= 0)
            {
                ShowError("Invalid font size.");
                return;
            }

            var settings = new Settings
            {
                Items = Reminders.ToList(),
                StartFontSize = fontSize,
                IsGlowEnabled = GlowCheckBox.IsChecked == true,
                IsSpotifyMode = isSpotify
            };

            _noteWindow?.Close();
            _noteWindow = new FloatingNoteWindow(settings);
            _noteWindow.Show();
            this.Hide();
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void CreateTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Floating Note"
            };

            var contextMenu = new ContextMenu();
            contextMenu.MenuItems.Add("Show Dashboard", OnShowDashboard);
            contextMenu.MenuItems.Add("Exit", OnExitApplication);

            _notifyIcon.ContextMenu = contextMenu;
            _notifyIcon.DoubleClick += OnShowDashboard;
        }

        private void OnShowDashboard(object sender, EventArgs e)
        {
            this.Show();
            this.Activate();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            OnExitApplication(null, EventArgs.Empty);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputMsg.Text)) return;
            if (!int.TryParse(InputSec.Text, out int s)) s = 5;

            if (_editingItem != null)
            {
                _editingItem.Message = InputMsg.Text;
                _editingItem.DurationSeconds = s;
                int idx = Reminders.IndexOf(_editingItem);
                Reminders.RemoveAt(idx);
                Reminders.Insert(idx, _editingItem);
                _editingItem = null;
                AddUpdateBtn.Content = "ADD MESSAGE";
            }
            else
            {
                Reminders.Add(new ReminderItem { Message = InputMsg.Text, DurationSeconds = s });
            }
            InputMsg.Clear();
            InputSec.Text = "5";
        }

        private void EditBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is ReminderItem item)
            {
                InputMsg.Text = item.Message;
                InputSec.Text = item.DurationSeconds.ToString();
                _editingItem = item;
                AddUpdateBtn.Content = "UPDATE MESSAGE";
            }
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is ReminderItem item)
            {
                Reminders.Remove(item);
                if (_editingItem == item)
                {
                    _editingItem = null;
                    InputMsg.Clear();
                    InputSec.Text = "5";
                    AddUpdateBtn.Content = "ADD MESSAGE";
                }
            }
        }
    }
}
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms; // Requires System.Windows.Forms reference
using System.Drawing;       // Requires System.Drawing reference
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Forms.ContextMenu;
using System.Windows.Media;
using System.Windows.Input;

namespace FloatingNote
{
    public partial class MainWindow : Window
    {
        private NotifyIcon _notifyIcon;
        private FloatingNoteWindow _noteWindow;
        private int _currentGradientIndex = 0;

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

        private void GradientButton_Click(object sender, RoutedEventArgs e)
        {
            _currentGradientIndex = (_currentGradientIndex + 1) % GradientPresets.SpotifyLikeGradients.Count;
            this.Resources["DashboardBackgroundBrush"] = GradientPresets.SpotifyLikeGradients[_currentGradientIndex];
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            // Check if Spotify Mode is requested
            bool isSpotify = SpotifyCheckBox.IsChecked == true;

            // Only enforce "At least one message" if we are NOT in Spotify mode
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

            // Create settings with the new Spotify Flag
            var settings = new Settings
            {
                Items = Reminders.ToList(),
                StartFontSize = fontSize,
                IsGlowEnabled = GlowCheckBox.IsChecked == true,
                IsSpotifyMode = isSpotify // <--- PASSING THE FLAG
            };

            // Close existing window if open
            _noteWindow?.Close();

            // Launch the floating window
            _noteWindow = new FloatingNoteWindow(settings);
            _noteWindow.Show();

            // Hide dashboard
            this.Hide();
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }

        // --- APP LIFECYCLE ---

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CreateTrayIcon();
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

        private void OnExitApplication(object sender, EventArgs e)
        {
            _notifyIcon?.Dispose();
            _noteWindow?.Close();
            Application.Current.Shutdown();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            OnExitApplication(null, EventArgs.Empty);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            this.Hide();
        }

        // --- LIST MANAGEMENT (Add, Edit, Delete) ---

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputMsg.Text)) return;

            if (!int.TryParse(InputSec.Text, out int s)) s = 5;

            if (_editingItem != null)
            {
                _editingItem.Message = InputMsg.Text;
                _editingItem.DurationSeconds = s;

                // Refresh list view hack
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
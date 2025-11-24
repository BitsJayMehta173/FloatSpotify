using FloatingReminder;
using System;
using System.Windows;

namespace FloatingNote
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set shutdown mode to only exit when explicitly told (i.e., from the tray icon)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                // Ensure the font is downloaded before starting the main window
                await FontLoader.EnsureFontDownloadedAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to download the required font: {ex.Message}", "Font Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Optionally, shut down if the font is critical
                // Application.Current.Shutdown(); 
            }

            // Create and show the main dashboard window
            var dashboard = new MainWindow();
            dashboard.Show();
        }
    }
}
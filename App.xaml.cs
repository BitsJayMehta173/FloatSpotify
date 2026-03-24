using System.Diagnostics;
using System.Windows;

namespace FloatingNote
{
    public partial class App : Application
    {
        public static Process PythonProcess;

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (PythonProcess != null && !PythonProcess.HasExited)
                {
                    PythonProcess.Kill();
                    PythonProcess.Dispose();
                }
            }
            catch { }

            base.OnExit(e);
        }
    }
}
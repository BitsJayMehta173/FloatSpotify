using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Diagnostics;

namespace FloatingNote
{
    public static class FontLoader
    {
        private static readonly string FontDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");

        public static FontFamily NoteFontFamily { get; private set; }

        static FontLoader()
        {
            NoteFontFamily = new FontFamily("Segoe UI");
            Log("Default font (Segoe UI) initialized as fallback.");
        }

        public static async Task EnsureFontDownloadedAsync()
        {
            try
            {
                if (!Directory.Exists(FontDir))
                    Directory.CreateDirectory(FontDir);

                string toLoad = null;

                var fontFiles = Directory.GetFiles(FontDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(s => s.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                s.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (fontFiles.Length > 0)
                {
                    Log($"Found {fontFiles.Length} local font file(s).");

                    toLoad = fontFiles.FirstOrDefault(f =>
                        f.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) < 0 &&
                        f.IndexOf("Light", StringComparison.OrdinalIgnoreCase) < 0 &&
                        f.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) < 0);

                    if (string.IsNullOrEmpty(toLoad))
                        toLoad = fontFiles[0];
                }
                else
                {
                    Log("No local fonts found. Downloading fallback font.");

                    string fallbackPath = Path.Combine(FontDir, "DMSans-Regular.ttf");

                    if (!File.Exists(fallbackPath))
                    {
                        using (WebClient client = new WebClient())
                        {
                            await client.DownloadFileTaskAsync(
                                new Uri("https://fonts.gstatic.com/s/dmsans/v15/rP2Hp2ywxg089UriCZSCHBeH.ttf"),
                                fallbackPath);
                        }
                    }

                    toLoad = fallbackPath;
                }

                if (toLoad != null && File.Exists(toLoad))
                {
                    var uri = new Uri(toLoad, UriKind.Absolute);
                    var families = Fonts.GetFontFamilies(uri, "./");

                    if (!families.Any())
                        families = Fonts.GetFontFamilies(
                            new Uri($"file:///{toLoad.Replace("\\", "/")}"));

                    foreach (var family in families)
                    {
                        NoteFontFamily = family;
                        Log($"Loaded Font Family: {NoteFontFamily.Source}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Font load failed: {ex.Message}");
            }
        }

        private static void Log(string message)
        {
            string log = $"[FontLoader] {message}";
            Debug.WriteLine(log);
            Console.WriteLine(log);
        }
    }
}
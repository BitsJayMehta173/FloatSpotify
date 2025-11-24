using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Diagnostics; // Needed for Debug.WriteLine

namespace FloatingNote
{
    public static class FontLoader
    {
        // Use a more robust way to get the base directory for WPF apps
        private static readonly string FontDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");

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

                // 1. SEARCH: Scan the Fonts folder for user-provided fonts
                var fontFiles = Directory.GetFiles(FontDir, "*.*", SearchOption.TopDirectoryOnly)
                                         .Where(s => s.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                                     s.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                                         .ToArray();

                if (fontFiles.Length > 0)
                {
                    Log($"Found {fontFiles.Length} local font file(s) in Fonts folder.");

                    // Try to find the "regular" version first.
                    // We use IndexOf >= 0 to check if "Bold", "Light", etc. exist in the filename.
                    // If IndexOf returns -1, the substring is NOT found.
                    toLoad = fontFiles.FirstOrDefault(f =>
                        f.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) < 0 &&
                        f.IndexOf("Light", StringComparison.OrdinalIgnoreCase) < 0 &&
                        f.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) < 0);

                    // If no "regular" found, just take the first available font file.
                    if (string.IsNullOrEmpty(toLoad))
                    {
                        toLoad = fontFiles[0];
                        Log("No 'Regular' font found, using first available font.");
                    }

                    Log($"Selected local font file to load: {Path.GetFileName(toLoad)}");
                }
                else
                {
                    Log("No local fonts found in Fonts folder. Downloading fallback (DM Sans)...");
                    // 2. FALLBACK: Download DM Sans if folder is empty
                    string fallbackPath = Path.Combine(FontDir, "DMSans-Regular.ttf");
                    if (!File.Exists(fallbackPath))
                    {
                        using (WebClient client = new WebClient())
                        {
                            await client.DownloadFileTaskAsync(new Uri("https://fonts.gstatic.com/s/dmsans/v15/rP2Hp2ywxg089UriCZSCHBeH.ttf"), fallbackPath);
                        }
                        Log("Fallback font downloaded successfully.");
                    }
                    toLoad = fallbackPath;
                }

                // 3. LOAD: Actually load the selected font into WPF
                if (toLoad != null && File.Exists(toLoad))
                {
                    // WPF needs a URI to the file to load it dynamically.
                    // We use absolute path to be safe.
                    var uri = new Uri(toLoad, UriKind.Absolute);
                    var families = Fonts.GetFontFamilies(uri, "./");

                    // If the above doesn't work, sometimes just passing the file URI works better for single files
                    if (!families.Any())
                    {
                        families = Fonts.GetFontFamilies(new Uri($"file:///{toLoad.Replace("\\", "/")}"));
                    }

                    foreach (var family in families)
                    {
                        NoteFontFamily = family;
                        Log($"[SUCCESS] Loaded Font Family: '{NoteFontFamily.Source}'");
                        return;
                    }

                    Log("[WARNING] Font file found but no FontFamily could be extracted.");
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to load font: {ex.Message}");
                // Fallback remains Segoe UI
            }
        }

        // Helper to log to both Debug output and Console
        private static void Log(string message)
        {
            string log = $"[FontLoader] {message}";
            Debug.WriteLine(log);
            Console.WriteLine(log);
        }
    }
}
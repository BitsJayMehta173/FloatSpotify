using System;
using System.Collections.Generic;
using System.Net.Http; // Required for connecting to Python
using System.Text.Json; // Required for parsing JSON
using System.Threading; // Required for CancellationToken
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FloatingNote
{
    public partial class FloatingNoteWindow : Window
    {
        // --- WINDOW STATE ---
        private bool _isDragging;
        private Point _dragStart;

        // --- TIMERS ---
        private readonly DispatcherTimer _textChangeTimer; // Standard Reminder Timer
        private readonly DispatcherTimer _spotifyTimer;    // High-speed Spotify Polling Timer

        // --- DATA ---
        private int _currentIndex;
        private readonly List<ReminderItem> _items;
        private readonly bool _isSpotifyMode;

        // --- SPOTIFY SPECIFIC ---
        private static readonly HttpClient _httpClient = new HttpClient(); // Static to prevent socket exhaustion
        private string _cachedLyric = "";

        // --- ANIMATIONS ---
        private readonly DoubleAnimation _fadeOutAnim;
        private readonly DoubleAnimation _fadeInAnim;

        public FloatingNoteWindow(Settings settings)
        {
            InitializeComponent();

            _isSpotifyMode = settings.IsSpotifyMode;
            _items = settings.Items ?? new List<ReminderItem>();

            // Fallback if list is empty
            if (_items.Count == 0) _items.Add(new ReminderItem { Message = "No messages.", DurationSeconds = 10 });

            // 1. VISUAL SETUP
            ReminderText.FontSize = settings.StartFontSize;
            if (!settings.IsGlowEnabled) ReminderText.Effect = null;

            // Allow wrapping for long lyrics
            ReminderText.TextWrapping = TextWrapping.Wrap;
            ReminderText.TextAlignment = TextAlignment.Center;

            // 2. ANIMATION SETUP
            _fadeOutAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            _fadeOutAnim.Completed += FadeOutAnim_Completed;

            _fadeInAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            // 3. MODE SELECTION
            if (_isSpotifyMode)
            {
                // --- SPOTIFY MODE ---
                ReminderText.Text = "Connecting to Spotify...";

                // Configure fast polling (every 150ms) for smooth sync
                _spotifyTimer = new DispatcherTimer(DispatcherPriority.Render);
                _spotifyTimer.Interval = TimeSpan.FromMilliseconds(150);
                _spotifyTimer.Tick += SpotifyTimer_Tick;
                _spotifyTimer.Start();
            }
            else
            {
                // --- NORMAL REMINDER MODE ---
                _currentIndex = 0;
                ReminderText.Text = _items[0].Message;

                // Configure standard timer
                _textChangeTimer = new DispatcherTimer(DispatcherPriority.Background);
                _textChangeTimer.Tick += TextChangeTimer_Tick;

                if (_items.Count > 1)
                {
                    ResetTimerForCurrentItem();
                }
            }
        }

        // =========================================================
        // SPOTIFY SYNC LOGIC
        // =========================================================
        private async void SpotifyTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                using (var cts = new CancellationTokenSource(100))
                {
                    // 1. GET STATUS FROM PYTHON
                    var response = await _httpClient.GetAsync("http://127.0.0.1:8888/status", cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var data = JsonSerializer.Deserialize<SpotifyState>(json);

                        if (data != null)
                        {
                            string newText = data.current_lyric;

                            if (!data.is_playing)
                            {
                                newText = $"⏸ Paused: {data.track}";
                            }

                            if (newText != _cachedLyric)
                            {
                                _cachedLyric = newText;
                                ReminderText.BeginAnimation(OpacityProperty, _fadeOutAnim);
                            }
                        }
                    }
                }
            }
            catch
            {
                if (ReminderText.Text != "Waiting for Sync Server...")
                {
                    _cachedLyric = "Waiting for Sync Server...";
                    ReminderText.BeginAnimation(OpacityProperty, _fadeOutAnim);
                }
            }
        }

        // =========================================================
        // NORMAL REMINDER LOGIC
        // =========================================================
        private void TextChangeTimer_Tick(object sender, EventArgs e)
        {
            ReminderText.BeginAnimation(OpacityProperty, _fadeOutAnim);
        }

        private void ResetTimerForCurrentItem()
        {
            if (_textChangeTimer == null) return;
            _textChangeTimer.Stop();
            _textChangeTimer.Interval = TimeSpan.FromSeconds(_items[_currentIndex].DurationSeconds);
            _textChangeTimer.Start();
        }

        // =========================================================
        // SHARED ANIMATION HANDLER
        // =========================================================
        private void FadeOutAnim_Completed(object sender, EventArgs e)
        {
            if (_isSpotifyMode)
            {
                ReminderText.Text = _cachedLyric;
            }
            else
            {
                _currentIndex = (_currentIndex + 1) % _items.Count;
                ReminderText.Text = _items[_currentIndex].Message;
                ResetTimerForCurrentItem();
            }

            ReminderText.BeginAnimation(OpacityProperty, _fadeInAnim);
        }

        // =========================================================
        // WINDOW EVENTS (Unchanged from your previous version)
        // =========================================================
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                double newSize = ReminderText.FontSize + (e.Delta > 0 ? 2 : -2);
                newSize = Math.Max(10, Math.Min(400, newSize));
                ReminderText.FontSize = newSize;
                UpdateCloseButtonSize();
            }
        }

        private void UpdateCloseButtonSize()
        {
            double currentSize = ReminderText.FontSize;
            var anim = new DoubleAnimation(currentSize * 0.4, TimeSpan.FromMilliseconds(150));
            CloseButton.BeginAnimation(WidthProperty, anim);
            CloseButton.BeginAnimation(HeightProperty, anim);
        }

        private void Window_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            if (e.DeltaManipulation.Scale.X != 1.0)
            {
                double newSize = ReminderText.FontSize * e.DeltaManipulation.Scale.X;
                newSize = Math.Max(10, Math.Min(400, newSize));
                ReminderText.FontSize = newSize;
                UpdateCloseButtonSize();
            }
            DragTranslate.X += e.DeltaManipulation.Translation.X;
            DragTranslate.Y += e.DeltaManipulation.Translation.Y;
        }

        private void TextArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(this);
            TextArea.CaptureMouse();
        }

        private void TextArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            Point current = e.GetPosition(this);
            DragTranslate.X += current.X - _dragStart.X;
            DragTranslate.Y += current.Y - _dragStart.Y;
            _dragStart = current;
        }

        private void TextArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            TextArea.ReleaseMouseCapture();
        }

        private void ButtonHitbox_MouseEnter(object sender, MouseEventArgs e) => CloseButton.Opacity = 1;
        private void ButtonHitbox_MouseLeave(object sender, MouseEventArgs e) => CloseButton.Opacity = 0;
    }

    public class SpotifyState
    {
        public bool is_playing { get; set; }
        public string track { get; set; }
        public string artist { get; set; }
        public string current_lyric { get; set; }
    }
}
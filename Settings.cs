using System.Collections.Generic;

namespace FloatingNote
{
    public class Settings
    {
        // Holds the list of reminders
        public List<ReminderItem> Items { get; set; }

        // Visual settings
        public double StartFontSize { get; set; }
        public bool IsGlowEnabled { get; set; }

        // NEW: Spotify Integration Toggle
        public bool IsSpotifyMode { get; set; }

        public Settings()
        {
            Items = new List<ReminderItem>
            {
                new ReminderItem { Message = "Welcome to your new dashboard! ✨", DurationSeconds = 5 },
                new ReminderItem { Message = "Add your own messages below 👇", DurationSeconds = 8 }
            };
            StartFontSize = 60;
            IsGlowEnabled = true;
            IsSpotifyMode = false; // Default to standard mode
        }
    }
}
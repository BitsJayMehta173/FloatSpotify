namespace FloatingNote
{
    public class ReminderItem
    {
        public string Message { get; set; }
        // How many seconds THIS specific message should stay on screen
        public int DurationSeconds { get; set; } = 5;

        // Helper for display in the list if needed, though we use data templates now
        public override string ToString()
        {
            return $"{Message} ({DurationSeconds}s)";
        }
    }
}
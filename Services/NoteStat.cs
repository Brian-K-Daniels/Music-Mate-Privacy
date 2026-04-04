using SQLite;

namespace musicmate.Services
{
    public class NoteStat
    {
        // Add property for reliable XAML binding
        [Ignore]
        public Microsoft.Maui.Graphics.Color ContrastingTextColor { get; set; } = Microsoft.Maui.Graphics.Colors.Red;
        [PrimaryKey]
        public string WrittenName { get; set; } = string.Empty;
        public int Correct { get; set; }
        public int Wrong { get; set; }
        public string? Accidental { get; set; }
        public string? NoteLetter { get; set; }
        public int? Octave { get; set; }
        public double MsAverage { get; set; }
        public int MsCount { get; set; }
        public int CorrectCount => Correct;
        public int WrongCount => Wrong;

        public double PercentCorrect =>
            (Correct + Wrong) == 0 ? 0.0 : (double)Correct / (Correct + Wrong) * 100.0;
    }
}

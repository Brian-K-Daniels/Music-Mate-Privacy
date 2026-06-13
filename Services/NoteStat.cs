using SQLite;

namespace musicmate.Services
{
    public class NoteStat
    {
        [Ignore]
        public Microsoft.Maui.Graphics.Color ContrastingTextColor { get; set; } = Microsoft.Maui.Graphics.Colors.Red;

        [PrimaryKey]
        public string WrittenName { get; set; } = string.Empty;

        /// <summary>Legacy overall correct count (mirrors OverallCorrectCount).</summary>
        public int Correct { get; set; }

        /// <summary>Legacy overall wrong count (mirrors OverallWrongCount).</summary>
        public int Wrong { get; set; }

        public int PitchCorrectCount { get; set; }
        public int PitchWrongCount { get; set; }
        public int TimingCorrectCount { get; set; }
        public int TimingWrongCount { get; set; }
        public int OverallCorrectCount { get; set; }
        public int OverallWrongCount { get; set; }
        public int RestCorrectCount { get; set; }
        public int RestWrongCount { get; set; }

        public string? Accidental { get; set; }
        public string? NoteLetter { get; set; }
        public int? Octave { get; set; }
        public double MsAverage { get; set; }
        public int MsCount { get; set; }
        public int Streak { get; set; }

        public int CorrectCount => Correct;
        public int WrongCount => Wrong;

        [Ignore]
        public double PercentPitchCorrect =>
            (PitchCorrectCount + PitchWrongCount) == 0
                ? 0.0
                : (double)PitchCorrectCount / (PitchCorrectCount + PitchWrongCount) * 100.0;

        [Ignore]
        public double PercentTimingCorrect =>
            (TimingCorrectCount + TimingWrongCount) == 0
                ? 0.0
                : (double)TimingCorrectCount / (TimingCorrectCount + TimingWrongCount) * 100.0;

        [Ignore]
        public double PercentOverallCorrect =>
            (OverallCorrectCount + OverallWrongCount) == 0
                ? 0.0
                : (double)OverallCorrectCount / (OverallCorrectCount + OverallWrongCount) * 100.0;

        /// <summary>Legacy percent — uses overall accuracy.</summary>
        public double PercentCorrect => PercentOverallCorrect;
    }
}

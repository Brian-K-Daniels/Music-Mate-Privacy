using SQLite;

namespace musicmate.Services
{
    public class NoteStat
    {
        [Ignore]
        public Microsoft.Maui.Graphics.Color ContrastingTextColor { get; set; } = Microsoft.Maui.Graphics.Colors.Red;

        [Ignore]
        public string MasteredDisplay { get; set; } = string.Empty;

        /// <summary>Persisted mastery snapshot (0/1) at last stats save using current mastery settings.</summary>
        [Column("Mstrd")]
        public int Mastered { get; set; }

        [PrimaryKey]
        public string WrittenName { get; set; } = string.Empty;

        /// <summary>Legacy overall correct count (mirrors OverallCorrectCount).</summary>
        public int Correct { get; set; }

        /// <summary>Legacy overall wrong count (mirrors OverallWrongCount).</summary>
        public int Wrong { get; set; }

        [Column("PchRt")]
        public int PitchCorrectCount { get; set; }
        [Column("PchWr")]
        public int PitchWrongCount { get; set; }
        [Column("TmgRt")]
        public int TimingCorrectCount { get; set; }
        [Column("TmgWr")]
        public int TimingWrongCount { get; set; }
        [Column("OvrRt")]
        public int OverallCorrectCount { get; set; }
        [Column("OvrWr")]
        public int OverallWrongCount { get; set; }
        [Column("RstRt")]
        public int RestCorrectCount { get; set; }
        [Column("RstWr")]
        public int RestWrongCount { get; set; }

        public string? Accidental { get; set; }
        public string? NoteLetter { get; set; }
        public int? Octave { get; set; }
        public double MsAverage { get; set; }
        public int MsCount { get; set; }
        public int Streak { get; set; }

        //public int CorrectCount => Correct;  //  2026.07.07 1623  out 2 lines
        //public int WrongCount => Wrong;

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

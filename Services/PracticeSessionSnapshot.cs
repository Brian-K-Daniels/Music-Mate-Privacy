using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>Notes plus generation/staff state saved for Repeat Same.</summary>
    public sealed class PracticeSessionSnapshot
    {
        public List<NoteInfo> Notes { get; init; } = new();
        public string Key { get; init; } = "C";
        public string SelectedScale { get; init; } = "Major";
        public string EffectiveScale { get; init; } = "Major";
        public ScaleSelectionMode ScaleSelectionMode { get; init; }
        public bool IsRandomMode { get; init; }
        public string Tune { get; init; } = "";
        public List<GeneratedNote> UpperNotes { get; init; } = new();
        public List<GeneratedNote> LowerNotes { get; init; } = new();
        public List<double> UpperBarBeats { get; init; } = new();
        public List<double> LowerBarBeats { get; init; } = new();
        public int UpperPitchCount { get; init; }
        public bool UpperHasEndBar { get; init; }
    }
}

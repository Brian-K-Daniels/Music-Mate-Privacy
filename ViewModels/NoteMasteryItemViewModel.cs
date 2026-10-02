using System.ComponentModel;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.ViewModels;

/// <summary>One written note on the Note Mastery staff / detail panel.</summary>
public sealed class NoteMasteryItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string WrittenNoteName { get; init; }
    public required int MidiNumber { get; init; }
    public required NoteMasteryState MasteryState { get; init; }
    public required string InstrumentDisplayName { get; init; }

    public int AttemptCount { get; init; }
    public int CorrectAttempts { get; init; }
    public int IncorrectAttempts { get; init; }
    public double? PitchAccuracyPercent { get; init; }
    public double? TimingAccuracyPercent { get; init; }
    public int? LongestStreak { get; init; }
    public DateTime? LastPracticedUtc { get; init; }

    public string StateDisplayName => NoteMasteryStateLabels.DisplayName(MasteryState);
    public string StateMarker => NoteMasteryStateLabels.Marker(MasteryState);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static NoteMasteryItemViewModel FromStat(
        string writtenName,
        int midi,
        string instrumentDisplayName,
        NoteStat? stat,
        NoteMasteryState state,
        DateTime? lastPracticedUtc)
    {
        int correct = stat?.OverallCorrectCount ?? stat?.Correct ?? 0;
        int wrong = stat?.OverallWrongCount ?? stat?.Wrong ?? 0;
        int attempts = correct + wrong;
        if (attempts == 0 && stat is not null)
            attempts = NoteMasteryClassifier.TotalAttempts(stat);

        double? pitch = null;
        double? timing = null;
        int? streak = null;

        if (stat is not null && attempts > 0)
        {
            int pitchTotal = stat.PitchCorrectCount + stat.PitchWrongCount;
            if (pitchTotal > 0)
                pitch = stat.PercentPitchCorrect;

            int timingTotal = stat.TimingCorrectCount + stat.TimingWrongCount;
            if (timingTotal > 0)
                timing = stat.PercentTimingCorrect;

            streak = stat.Streak;
        }

        return new NoteMasteryItemViewModel
        {
            WrittenNoteName = writtenName,
            MidiNumber = midi,
            MasteryState = state,
            InstrumentDisplayName = instrumentDisplayName,
            AttemptCount = attempts,
            CorrectAttempts = correct,
            IncorrectAttempts = wrong,
            PitchAccuracyPercent = pitch,
            TimingAccuracyPercent = timing,
            LongestStreak = streak,
            LastPracticedUtc = lastPracticedUtc,
        };
    }
}

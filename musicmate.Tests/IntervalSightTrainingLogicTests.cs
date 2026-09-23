using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class IntervalSightTrainingLogicTests
{
    [Theory]
    [InlineData(60, 64, 4)]
    [InlineData(64, 60, 4)]
    [InlineData(60, 72, 12)]
    [InlineData(72, 60, 12)]
    [InlineData(60, 60, 0)]
    public void AbsoluteSemitones_IgnoresDirection(int a, int b, int expected)
    {
        Assert.Equal(expected, IntervalSightTrainingLogic.AbsoluteSemitones(Pitched(a), Pitched(b)));
    }

    [Fact]
    public void FindNext_SkipsUntestableLeap_ResumesAtNextNote()
    {
        var notes = new List<GeneratedNote>
        {
            Pitched(60, measure: 0, beat: 0),
            Pitched(73, measure: 0, beat: 1), // +13
            Pitched(75, measure: 0, beat: 2), // +2 from B
        };
        var pitched = new List<int> { 0, 1, 2 };

        var first = IntervalSightTrainingLogic.FindNextTestablePairInMeasure(notes, pitched, 0);
        Assert.NotNull(first);
        Assert.Equal(1, first.Value.AnchorPos);
        Assert.Equal(2, first.Value.TargetPos);
        Assert.Equal(2, first.Value.AbsoluteSemitones);
    }

    [Fact]
    public void DescendingInterval_AcceptedByMagnitudeButton()
    {
        var notes = new List<GeneratedNote>
        {
            Pitched(67, 0, 0),
            Pitched(60, 0, 1), // down 7
        };
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.Equal(7, logic.CurrentPair!.Value.AbsoluteSemitones);
        Assert.True(logic.SubmitAnswer(7));
        Assert.True(logic.IsSessionComplete);
    }

    [Fact]
    public void Rests_AreIgnoredWhenPairing()
    {
        var notes = new List<GeneratedNote>
        {
            Pitched(60, 0, 0),
            Rest(0, 1),
            Pitched(64, 0, 2),
        };
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.True(logic.HasCurrentPair);
        var pair = logic.CurrentPair!.Value;
        Assert.Equal(0, pair.AnchorFlatIndex);
        Assert.Equal(2, pair.TargetFlatIndex);
        Assert.Equal(4, pair.AbsoluteSemitones);
    }

    [Fact]
    public void PairsAcrossBarLine_WhenMelodyIsConsecutive()
    {
        var notes = new List<GeneratedNote>
        {
            Pitched(60, 0, 0),
            Pitched(62, 0, 1),
            Pitched(64, 1, 4),
            Pitched(65, 1, 5),
        };
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.Equal(0, logic.CurrentPair!.Value.AnchorFlatIndex);
        Assert.Equal(1, logic.CurrentPair!.Value.TargetFlatIndex);

        Assert.True(logic.SubmitAnswer(2));
        Assert.True(logic.HasCurrentPair);
        Assert.Equal(1, logic.CurrentPair!.Value.AnchorFlatIndex);
        Assert.Equal(2, logic.CurrentPair!.Value.TargetFlatIndex);
        Assert.Equal(2, logic.CurrentPair!.Value.AbsoluteSemitones);

        Assert.True(logic.SubmitAnswer(2));
        Assert.Equal(2, logic.CurrentPair!.Value.AnchorFlatIndex);
        Assert.Equal(3, logic.CurrentPair!.Value.TargetFlatIndex);
        Assert.Equal(1, logic.CurrentPair!.Value.AbsoluteSemitones);
    }

    [Fact]
    public void WrongAnswer_DoesNotAdvance_ThenCorrectDoes()
    {
        var notes = new List<GeneratedNote>
        {
            Pitched(60, 0, 0),
            Pitched(64, 0, 1),
            Pitched(67, 0, 2),
        };
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.False(logic.SubmitAnswer(3));
        Assert.True(logic.WrongThisPair);
        Assert.Equal(0, logic.Stats.IntervalsTested);
        Assert.Equal(1, logic.Stats.IncorrectAttempts);

        var states = logic.BuildNoteStates();
        Assert.Equal(StaffNoteState.Wrong, states[0]);
        Assert.Equal(StaffNoteState.Wrong, states[1]);

        Assert.True(logic.SubmitAnswer(4));
        Assert.Equal(1, logic.Stats.IntervalsTested);
        Assert.Equal(0, logic.Stats.CorrectOnFirstAttempt);
        Assert.Equal(1, logic.CurrentPair!.Value.AnchorFlatIndex);
        Assert.Equal(2, logic.CurrentPair!.Value.TargetFlatIndex);
    }

    [Fact]
    public void FirstAttemptCorrect_IsTracked()
    {
        var notes = new List<GeneratedNote>
        {
            Pitched(60, 0, 0),
            Pitched(62, 0, 1),
            Pitched(64, 0, 2),
        };
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.True(logic.SubmitAnswer(2));
        Assert.True(logic.SubmitAnswer(2));
        Assert.True(logic.IsSessionComplete);
        Assert.Equal(2, logic.Stats.IntervalsTested);
        Assert.Equal(2, logic.Stats.CorrectOnFirstAttempt);
        Assert.Equal(0, logic.Stats.IncorrectAttempts);
        Assert.Equal(100.0, logic.Stats.FirstAttemptPercent);
    }

    [Fact]
    public void UntestablePairs_NotCountedInStats()
    {
        var notes = new List<GeneratedNote>
        {
            Pitched(60, 0, 0),
            Pitched(73, 0, 1),
            Pitched(75, 0, 2),
        };
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.Equal(1, logic.CurrentPair!.Value.AnchorFlatIndex);
        Assert.True(logic.SubmitAnswer(2));
        Assert.True(logic.IsSessionComplete);
        Assert.Equal(1, logic.Stats.IntervalsTested);
    }

    [Fact]
    public void ConsecutivePitchedNotes_PairAcrossRestAndBar()
    {
        var notes = new List<GeneratedNote>
        {
            Pitched(60, 0, 0),
            Rest(0, 1),
            Pitched(60, 1, 4),
            Pitched(62, 1, 5),
        };
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.Equal(0, logic.CurrentPair!.Value.AnchorFlatIndex);
        Assert.Equal(2, logic.CurrentPair!.Value.TargetFlatIndex);
        Assert.Equal(0, logic.CurrentPair!.Value.AbsoluteSemitones);
    }

    [Fact]
    public void CSharpOnStaff_IsMinorSecond_EvenWhenStoredMidiIsMajorSecond()
    {
        // Staff: C4 then C♯4 (minor second). Stale MIDI 60→62 would quiz a major second.
        var notes = new List<GeneratedNote>
        {
            Spelled(60, 'C', 4, Accidental.None, "C4"),
            Spelled(62, 'C', 4, Accidental.Sharp, "C#4"),
        };
        var logic = new IntervalSightTrainingLogic(notes, "G", "Major");
        Assert.Equal(1, logic.CurrentPair!.Value.AbsoluteSemitones);
        Assert.False(logic.SubmitAnswer(2));
        Assert.True(logic.SubmitAnswer(1));
        Assert.True(logic.IsSessionComplete);
    }

    [Fact]
    public void KeySignatureFSharp_ToG_IsMinorSecond()
    {
        // G major: F on the F line is F♯. F♯→G is one semitone, not F♮→G.
        var notes = new List<GeneratedNote>
        {
            Spelled(65, 'F', 4, Accidental.None, "F4"),
            Spelled(67, 'G', 4, Accidental.None, "G4"),
        };
        var logic = new IntervalSightTrainingLogic(notes, "G", "Major");
        Assert.Equal(1, logic.CurrentPair!.Value.AbsoluteSemitones);
        Assert.True(logic.SubmitAnswer(1));
    }

    [Fact]
    public void BuildNoteStates_YellowPair_ThenGreenAdvance()
    {
        var notes = new List<GeneratedNote>
        {
            Pitched(60, 0, 0),
            Pitched(62, 0, 1),
            Pitched(64, 0, 2),
        };
        var logic = new IntervalSightTrainingLogic(notes);
        var s0 = logic.BuildNoteStates();
        Assert.Equal(StaffNoteState.Current, s0[0]);
        Assert.Equal(StaffNoteState.Current, s0[1]);
        Assert.Equal(StaffNoteState.Pending, s0[2]);

        Assert.True(logic.SubmitAnswer(2));
        var s1 = logic.BuildNoteStates();
        Assert.Equal(StaffNoteState.Correct, s1[0]);
        Assert.Equal(StaffNoteState.Current, s1[1]);
        Assert.Equal(StaffNoteState.Current, s1[2]);
    }

    private static GeneratedNote Pitched(int midi, int measure = 0, double beat = 0)
        => new()
        {
            MidiNumber = midi,
            Letter = 'C',
            Octave = midi / 12 - 1,
            Accidental = Accidental.None,
            SpelledName = $"N{midi}",
            Duration = NoteDuration.Quarter,
            IsRest = false,
            MeasureIndex = measure,
            BeatPosition = beat,
            TargetFrequency = 440,
        };

    private static GeneratedNote Spelled(
        int midi, char letter, int octave, Accidental accidental, string spelledName,
        int measure = 0, double beat = 0)
        => new()
        {
            MidiNumber = midi,
            Letter = letter,
            Octave = octave,
            Accidental = accidental,
            SpelledName = spelledName,
            Duration = NoteDuration.Quarter,
            IsRest = false,
            MeasureIndex = measure,
            BeatPosition = beat,
            TargetFrequency = 440,
        };

    private static GeneratedNote Rest(int measure, double beat)
        => GeneratedNote.Rest(NoteDuration.Quarter, measure, beat);
}

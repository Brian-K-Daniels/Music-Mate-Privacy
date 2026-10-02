using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// End-of-sequence invariants for Interval Sight Training quiz state.
/// </summary>
public class IntervalSightTrainingTerminalStateTests
{
    [Fact]
    public void MiddleTransition_AdvancesToNextPair_WithinBounds()
    {
        var notes = ThreeAscending();
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.True(logic.HasCurrentPair);
        var first = logic.CurrentPair!.Value;
        Assert.InRange(first.AnchorFlatIndex, 0, notes.Count - 1);
        Assert.InRange(first.TargetFlatIndex, 0, notes.Count - 1);
        Assert.True(first.TargetFlatIndex > first.AnchorFlatIndex);

        Assert.True(logic.SubmitAnswer(first.AbsoluteSemitones));
        Assert.True(logic.HasCurrentPair);
        Assert.False(logic.IsSessionComplete);
        var second = logic.CurrentPair!.Value;
        Assert.InRange(second.AnchorFlatIndex, 0, notes.Count - 1);
        Assert.InRange(second.TargetFlatIndex, 0, notes.Count - 1);
        Assert.Equal(first.TargetFlatIndex, second.AnchorFlatIndex);
    }

    [Fact]
    public void FinalValidPair_Exists_AndIndicesInRange()
    {
        var notes = ThreeAscending();
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.True(logic.SubmitAnswer(2));
        Assert.True(logic.HasCurrentPair);
        var final = logic.CurrentPair!.Value;
        Assert.Equal(1, final.AnchorFlatIndex);
        Assert.Equal(2, final.TargetFlatIndex);
        Assert.True(final.TargetFlatIndex < notes.Count);
    }

    [Fact]
    public void CorrectOnFinalPair_EntersCompletedState()
    {
        var notes = ThreeAscending();
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.True(logic.SubmitAnswer(2));
        Assert.True(logic.SubmitAnswer(2));
        Assert.True(logic.IsSessionComplete);
        Assert.False(logic.HasCurrentPair);
        Assert.Null(logic.CurrentPair);
    }

    [Fact]
    public void NoNoteIndex_ExceedsNoteCountMinusOne()
    {
        var notes = FourNoteChain();
        var logic = new IntervalSightTrainingLogic(notes);
        while (logic.HasCurrentPair)
        {
            var p = logic.CurrentPair!.Value;
            Assert.InRange(p.AnchorFlatIndex, 0, notes.Count - 1);
            Assert.InRange(p.TargetFlatIndex, 0, notes.Count - 1);
            Assert.True(logic.SubmitAnswer(p.AbsoluteSemitones));
        }

        Assert.True(logic.IsSessionComplete);
    }

    [Fact]
    public void AfterCompletion_NoCurrentHighlight_AndNoExtraPair()
    {
        var notes = ThreeAscending();
        var logic = new IntervalSightTrainingLogic(notes);
        while (logic.HasCurrentPair)
            Assert.True(logic.SubmitAnswer(logic.CurrentPair!.Value.AbsoluteSemitones));

        var states = logic.BuildNoteStates();
        Assert.Equal(notes.Count, states.Length);
        Assert.DoesNotContain(StaffNoteState.Current, states);
        Assert.DoesNotContain(StaffNoteState.Wrong, states);
        Assert.Equal(StaffNoteState.Correct, states[0]);
        Assert.Equal(StaffNoteState.Correct, states[1]);
        Assert.Equal(StaffNoteState.Correct, states[2]);
    }

    [Fact]
    public void YellowHighlights_OnlyReferenceExistingNotes()
    {
        var notes = FourNoteChain();
        var logic = new IntervalSightTrainingLogic(notes);
        while (logic.HasCurrentPair)
        {
            var states = logic.BuildNoteStates();
            Assert.Equal(notes.Count, states.Length);
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] is StaffNoteState.Current or StaffNoteState.Wrong)
                    Assert.InRange(i, 0, notes.Count - 1);
            }

            var pair = logic.CurrentPair!.Value;
            Assert.Equal(StaffNoteState.Current, states[pair.AnchorFlatIndex]);
            Assert.Equal(StaffNoteState.Current, states[pair.TargetFlatIndex]);
            Assert.True(logic.SubmitAnswer(pair.AbsoluteSemitones));
        }
    }

    [Fact]
    public void OneNoteRemaining_AfterPenultimate_TerminatesSafely()
    {
        // Two notes only → one pair → correct completes with no phantom next.
        var notes = new List<GeneratedNote>
        {
            Pitched(60, 0, 0),
            Pitched(62, 0, 1),
        };
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.True(logic.HasCurrentPair);
        Assert.Equal(0, logic.CurrentPair!.Value.AnchorFlatIndex);
        Assert.Equal(1, logic.CurrentPair!.Value.TargetFlatIndex);
        Assert.True(logic.SubmitAnswer(2));
        Assert.True(logic.IsSessionComplete);
        Assert.False(logic.HasCurrentPair);
        var states = logic.BuildNoteStates();
        Assert.All(states, s => Assert.NotEqual(StaffNoteState.Current, s));
    }

    [Fact]
    public void FinalPairAcrossBarLine_CompletesWithoutPhantomNext()
    {
        var notes = new List<GeneratedNote>
        {
            Pitched(60, 0, 0),
            Pitched(62, 0, 1),
            Pitched(64, 1, 4),
        };
        var logic = new IntervalSightTrainingLogic(notes);
        Assert.True(logic.SubmitAnswer(2)); // 60→62
        Assert.True(logic.HasCurrentPair);
        Assert.Equal(1, logic.CurrentPair!.Value.AnchorFlatIndex);
        Assert.Equal(2, logic.CurrentPair!.Value.TargetFlatIndex);
        Assert.True(logic.SubmitAnswer(2)); // 62→64 across bar
        Assert.True(logic.IsSessionComplete);
        Assert.Null(logic.CurrentPair);
        var states = logic.BuildNoteStates();
        Assert.DoesNotContain(StaffNoteState.Current, states);
    }

    [Fact]
    public void ResetToFirstPair_ClearsCompletionAndHighlights()
    {
        var notes = ThreeAscending();
        var logic = new IntervalSightTrainingLogic(notes);
        while (logic.HasCurrentPair)
            Assert.True(logic.SubmitAnswer(logic.CurrentPair!.Value.AbsoluteSemitones));
        Assert.True(logic.IsSessionComplete);

        logic.ResetToFirstPair();
        Assert.False(logic.IsSessionComplete);
        Assert.True(logic.HasCurrentPair);
        var states = logic.BuildNoteStates();
        Assert.Equal(StaffNoteState.Current, states[0]);
        Assert.Equal(StaffNoteState.Current, states[1]);
        Assert.Equal(StaffNoteState.Pending, states[2]);
        Assert.Equal(0, logic.Stats.IntervalsTested);
    }

    [Fact]
    public void GeneratedLevel1Exercise_FullRun_NeverOutOfRange()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 1,
            AccidentalPercent = 0,
            Key = "G",
            SelectedScale = "Major",
            LowestNote = "C4",
            HighestNote = "C5",
        };
        var exercise = IntervalSightTrainingSequenceBuilder.Generate(
            session, measureCount: null, randomSeed: 42, childLevelOverride: 1);
        var logic = new IntervalSightTrainingLogic(exercise.Notes, exercise.Key, exercise.Scale);
        int noteCount = exercise.Notes.Count;
        Assert.True(noteCount >= 2);

        while (logic.HasCurrentPair)
        {
            var p = logic.CurrentPair!.Value;
            Assert.True(p.AnchorFlatIndex >= 0 && p.AnchorFlatIndex < noteCount);
            Assert.True(p.TargetFlatIndex >= 0 && p.TargetFlatIndex < noteCount);
            Assert.True(p.TargetFlatIndex < noteCount);
            Assert.True(logic.SubmitAnswer(p.AbsoluteSemitones));
        }

        Assert.True(logic.IsSessionComplete);
        var states = logic.BuildNoteStates();
        Assert.Equal(noteCount, states.Length);
        Assert.DoesNotContain(StaffNoteState.Current, states);
    }

    private static List<GeneratedNote> ThreeAscending()
        =>
        [
            Pitched(60, 0, 0),
            Pitched(62, 0, 1),
            Pitched(64, 0, 2),
        ];

    private static List<GeneratedNote> FourNoteChain()
        =>
        [
            Pitched(60, 0, 0),
            Pitched(62, 0, 1),
            Pitched(64, 0, 2),
            Pitched(65, 0, 3),
        ];

    private static GeneratedNote Pitched(int midi, int measure, double beat)
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
}

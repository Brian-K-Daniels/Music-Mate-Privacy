using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Interval Singing Training — Sing Interval staff must stay empty until correct or Reveal.
/// </summary>
public class IntervalSingingStaffDisplayTests
{
    private static IntervalEarTrainingLogic.IntervalPitches SampleExercise()
        => new(StartWrittenMidi: 60, EndWrittenMidi: 64, Semitones: 4, IsAscending: true);

    [Fact]
    public void NewInterval_SingInterval_StartsWithZeroDisplayedNotes()
    {
        var exercise = SampleExercise();
        Assert.Empty(IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            exercise,
            IntervalSingingExerciseState.Presenting,
            IntervalSingingTrainingMode.SingInterval));
        Assert.Empty(IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            exercise,
            IntervalSingingExerciseState.WaitingForSinger,
            IntervalSingingTrainingMode.SingInterval));
        Assert.Empty(IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            exercise,
            IntervalSingingExerciseState.Incorrect,
            IntervalSingingTrainingMode.SingInterval));
    }

    [Fact]
    public void CorrectResponse_ShowsBothTargetNotes()
    {
        var notes = IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            SampleExercise(),
            IntervalSingingExerciseState.Correct,
            IntervalSingingTrainingMode.SingInterval);
        Assert.Equal(2, notes.Count);
        Assert.Equal(60, notes[0].MidiNumber);
        Assert.Equal(64, notes[1].MidiNumber);
    }

    [Fact]
    public void Reveal_ShowsBothTargetNotes()
    {
        var notes = IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            SampleExercise(),
            IntervalSingingExerciseState.Revealed,
            IntervalSingingTrainingMode.SingInterval);
        Assert.Equal(2, notes.Count);
        Assert.Equal(60, notes[0].MidiNumber);
        Assert.Equal(64, notes[1].MidiNumber);
    }

    [Fact]
    public void CorrectAndReveal_UseSameTargetPair()
    {
        var exercise = SampleExercise();
        var correct = IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            exercise, IntervalSingingExerciseState.Correct, IntervalSingingTrainingMode.SingInterval);
        var revealed = IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            exercise, IntervalSingingExerciseState.Revealed, IntervalSingingTrainingMode.SingInterval);
        Assert.Equal(correct.Count, revealed.Count);
        Assert.Equal(correct[0].MidiNumber, revealed[0].MidiNumber);
        Assert.Equal(correct[1].MidiNumber, revealed[1].MidiNumber);
        Assert.Equal(correct[0].SpelledName, revealed[0].SpelledName);
        Assert.Equal(correct[1].SpelledName, revealed[1].SpelledName);
    }

    [Fact]
    public void NextInterval_ClearsStaffBackToZeroNotes()
    {
        var exercise = SampleExercise();
        Assert.Equal(2, IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            exercise, IntervalSingingExerciseState.Correct, IntervalSingingTrainingMode.SingInterval).Count);

        Assert.Empty(IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            exercise,
            IntervalSingingExerciseState.Presenting,
            IntervalSingingTrainingMode.SingInterval));
        Assert.Empty(IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            exercise,
            IntervalSingingExerciseState.WaitingForSinger,
            IntervalSingingTrainingMode.SingInterval));
    }

    [Fact]
    public void ResolveStaffDisplayNotes_DoesNotShowOnlyStartNote()
    {
        var notes = IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            SampleExercise(),
            IntervalSingingExerciseState.Correct,
            IntervalSingingTrainingMode.SingInterval);
        Assert.Equal(2, notes.Count);
        Assert.NotEqual(notes[0].MidiNumber, notes[1].MidiNumber);
    }

    [Fact]
    public void TransposedExercise_UsesRewrittenWrittenPitches()
    {
        var original = SampleExercise();
        var transposed = IntervalEarTrainingLogic.RetransposePreservingConcert(
            original, previousTransposeOffset: 0, newTransposeOffset: -2);
        var notes = IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            transposed,
            IntervalSingingExerciseState.Revealed,
            IntervalSingingTrainingMode.SingInterval);
        Assert.Equal(transposed.StartWrittenMidi, notes[0].MidiNumber);
        Assert.Equal(transposed.EndWrittenMidi, notes[1].MidiNumber);
    }

    [Theory]
    [InlineData(IntervalSingingExerciseState.Idle)]
    [InlineData(IntervalSingingExerciseState.Presenting)]
    [InlineData(IntervalSingingExerciseState.WaitingForSinger)]
    [InlineData(IntervalSingingExerciseState.Incorrect)]
    [InlineData(IntervalSingingExerciseState.Evaluating)]
    public void HiddenStates_NeverReturnNotes(IntervalSingingExerciseState state)
    {
        Assert.Empty(IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            SampleExercise(), state, IntervalSingingTrainingMode.SingInterval));
    }

    [Fact]
    public void CorrectReveal_StillShowsBothNotes_WhenSessionTuneIsTuner()
    {
        var notes = IntervalSingingTrainingLogic.ResolveStaffDisplayNotes(
            new IntervalEarTrainingLogic.IntervalPitches(60, 67, 7, true),
            IntervalSingingExerciseState.Correct,
            IntervalSingingTrainingMode.SingInterval);
        Assert.Equal(2, notes.Count);

        var session = new NoteSessionService
        {
            Tune = "Tuner",
            Key = "C",
            SelectedScale = "Major",
            ChildLevel = 23,
            MeterTimeSignature = "4/4",
            LowestNote = "C3",
            HighestNote = "C6",
        };

        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null)
        {
            SingleStaffLayout = true,
            OmitStaffHeader = true,
            NotationKeyOverride = IntervalEarTrainingNotation.StaffDisplayKey,
            NotationScaleOverride = IntervalEarTrainingNotation.StaffDisplayScale,
            UpperNotes = notes.ToList(),
            LowerNotes = new List<GeneratedNote>(),
            UpperBarBeats = IntervalEarTrainingNotation.BuildBarBeats(),
            UpperNoteStates = notes.Select(_ => StaffNoteState.Pending).ToArray(),
            AvailableHeight = 240f,
        };

        ApplyDrawStaffTunerGuard(drawable, session);

        Assert.Equal(2, drawable.UpperNotes.Count);
        Assert.Equal(60, drawable.UpperNotes[0].MidiNumber);
        Assert.Equal(67, drawable.UpperNotes[1].MidiNumber);
        Assert.Equal(2, drawable.UpperNoteStates?.Length ?? 0);
    }

    [Fact]
    public void TunerReferenceStaff_StillKeepsSingleNote_WhenMultipleUpperNotes()
    {
        var notes = IntervalEarTrainingNotation.BuildDisplayNotes(
            new IntervalEarTrainingLogic.IntervalPitches(60, 67, 7, true),
            IntervalEarTrainingNotation.StaffDisplayKey,
            IntervalEarTrainingNotation.StaffDisplayScale);

        var session = new NoteSessionService { Tune = "Tuner", ChildLevel = 23 };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null)
        {
            SingleStaffLayout = true,
            OmitStaffHeader = false,
            UpperNotes = notes,
            UpperNoteStates = new[] { StaffNoteState.Pending, StaffNoteState.Pending },
        };

        ApplyDrawStaffTunerGuard(drawable, session);

        Assert.Single(drawable.UpperNotes);
        Assert.Equal(60, drawable.UpperNotes[0].MidiNumber);
    }

    private static void ApplyDrawStaffTunerGuard(StaffDrawable drawable, NoteSessionService session)
    {
        if (session.Tune == "Tuner" && !drawable.OmitStaffHeader)
        {
            typeof(StaffDrawable)
                .GetMethod("EnforceTunerSingleNoteDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(drawable, null);
        }
    }
}

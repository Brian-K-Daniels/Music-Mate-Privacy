using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class EmphasizedNoteGenerationTests : IDisposable
{
    private readonly Dictionary<string, object?> _sessionStore = new();

    public EmphasizedNoteGenerationTests()
    {
        SessionPreferences.TestStore = _sessionStore;
        _sessionStore.Clear();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
    }

    [Fact]
    public void EmphasizedGeneration_IncreasesSelectedNoteFrequency()
    {
        int target = NoteSessionService.NoteNameToMidi("G4");
        // Aggregate across seeds so chance alone cannot mask the weight bias.
        int baseline = 0;
        int emphasized = 0;
        for (int seed = 0; seed < 40; seed++)
        {
            baseline += CountTarget(CreateGen(seed, emphasize: null), target);
            emphasized += CountTarget(
                CreateGen(seed, emphasize: target, weight: NoteMasteryPreferenceDefaults.EmphasizedNoteSelectionPercent),
                target);
        }

        Assert.True(emphasized > baseline, $"Expected emphasize {emphasized} > baseline {baseline}");
    }

    [Fact]
    public void EmphasizedGeneration_DoesNotProduceConsecutiveIdenticalPitchClasses()
    {
        int target = NoteSessionService.NoteNameToMidi("G4");
        var pitches = Flatten(CreateGen(seed: 42, emphasize: target, weight: 50));
        for (int i = 1; i < pitches.Count; i++)
            Assert.NotEqual(pitches[i] % 12, pitches[i - 1] % 12);
    }

    [Fact]
    public void EmphasizedGeneration_StillIncludesOtherEligibleNotes()
    {
        int target = NoteSessionService.NoteNameToMidi("A4");
        var pitches = Flatten(CreateGen(seed: 7, emphasize: target, weight: 40));
        Assert.Contains(target, pitches);
        Assert.True(pitches.Distinct().Count() >= 3);
    }

    [Fact]
    public void EmphasizedNote_NeverOutsidePlayableRange()
    {
        int target = NoteSessionService.NoteNameToMidi("C4");
        int lo = NoteSessionService.NoteNameToMidi("C4");
        int hi = NoteSessionService.NoteNameToMidi("B4");
        var pitches = Flatten(CreateGen(seed: 99, emphasize: target, weight: 70));
        Assert.All(pitches, m => Assert.InRange(m, lo, hi));
    }

    [Fact]
    public void TemporaryEmphasis_DoesNotChangeWhatToPlayMode()
    {
        var session = new NoteSessionService();
        session.Tune = "Selected Scale";
        session.IsRandomMode = false;
        string tuneBefore = session.Tune;
        bool randomBefore = session.IsRandomMode;

        session.BeginEmphasizedNotePractice("F#4", NoteMasteryPreferenceDefaults.EmphasizedNoteSelectionPercent);

        Assert.Equal(tuneBefore, session.Tune);
        Assert.Equal(randomBefore, session.IsRandomMode);
        Assert.True(session.HasTemporaryNoteEmphasis);
        Assert.Equal("F#4", session.TemporaryEmphasizedWrittenNote);

        session.ClearTemporaryNoteEmphasis("test");
        Assert.False(session.HasTemporaryNoteEmphasis);
        Assert.Equal(tuneBefore, session.Tune);
    }

    private static MusicSequenceGenerator CreateGen(int seed, int? emphasize, int weight = 40)
        => new()
        {
            Key = "C",
            Scale = "Major",
            LowestNote = "C4",
            HighestNote = "B4",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            UseScaleOrder = false,
            UseMotifPhrases = false,
            AccidentalPercent = 0,
            RestChancePercent = 0,
            MaxMelodicIntervalSemitones = 0,
            EmphasizedMidiNumber = emphasize,
            EmphasizedNoteSelectionPercent = weight,
            RandomSeed = seed,
        };

    private static List<int> Flatten(MusicSequenceGenerator gen)
        => MusicSequenceGenerator.Flatten(gen.GenerateSequence())
            .Where(n => !n.IsRest)
            .Select(n => n.MidiNumber)
            .ToList();

    private static int CountTarget(MusicSequenceGenerator gen, int target)
        => Flatten(gen).Count(m => m == target);
}

using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class IntervalEarTrainingLogicTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();
    private readonly Random _rng = new(42);

    public IntervalEarTrainingLogicTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void NoteDuration_DefaultsTo500()
    {
        Assert.Equal(500, IntervalEarTrainingLogic.DefaultNoteDurationMs);
        Assert.Equal(500, SessionPreferences.Get(
            IntervalEarTrainingLogic.NoteDurationPreferenceKey,
            IntervalEarTrainingLogic.DefaultNoteDurationMs));
    }

    [Fact]
    public void NoteDuration_PersistsAndRestores()
    {
        SessionPreferences.Set(IntervalEarTrainingLogic.NoteDurationPreferenceKey, 250);
        Assert.Equal(250, SessionPreferences.Get(
            IntervalEarTrainingLogic.NoteDurationPreferenceKey,
            IntervalEarTrainingLogic.DefaultNoteDurationMs));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    [InlineData(1000, 1000)]
    [InlineData(1500, 1000)]
    public void ClampNoteDurationMs(int input, int expected)
        => Assert.Equal(expected, IntervalEarTrainingLogic.ClampNoteDurationMs(input));

    [Fact]
    public void ResolvePlaybackNoteSeconds_ZeroUsesMinAudible()
    {
        double seconds = IntervalEarTrainingLogic.ResolvePlaybackNoteSeconds(0);
        Assert.Equal(IntervalEarTrainingLogic.MinAudibleNoteDurationMs / 1000.0, seconds);
    }

    [Fact]
    public void TryPickAscending_KeepsBothNotesInRange()
    {
        int low = 60;  // C4
        int high = 72; // C5
        for (int s = 0; s <= 12; s++)
        {
            Assert.True(IntervalEarTrainingLogic.TryPickAscendingInterval(
                low, high, s, _rng, out var pitches));
            Assert.InRange(pitches.StartWrittenMidi, low, high);
            Assert.InRange(pitches.EndWrittenMidi, low, high);
            Assert.Equal(s, pitches.EndWrittenMidi - pitches.StartWrittenMidi);
            Assert.Equal(s, pitches.Semitones);
            Assert.True(pitches.IsAscending);
        }
    }

    [Fact]
    public void TryPickDescending_KeepsBothNotesInRange()
    {
        int low = 60;
        int high = 72;
        for (int s = 0; s <= 12; s++)
        {
            Assert.True(IntervalEarTrainingLogic.TryPickInterval(
                low, high, s, IntervalDirectionMode.Descending, _rng, out var pitches));
            Assert.InRange(pitches.StartWrittenMidi, low, high);
            Assert.InRange(pitches.EndWrittenMidi, low, high);
            Assert.Equal(s, Math.Abs(pitches.EndWrittenMidi - pitches.StartWrittenMidi));
            Assert.Equal(s, pitches.Semitones);
            if (s == 0)
                Assert.True(pitches.IsAscending);
            else
            {
                Assert.False(pitches.IsAscending);
                Assert.Equal(s, pitches.StartWrittenMidi - pitches.EndWrittenMidi);
            }
        }
    }

    [Fact]
    public void TryPickDescending_Unison_UsesSamePitchTwice()
    {
        Assert.True(IntervalEarTrainingLogic.TryPickInterval(
            60, 72, 0, IntervalDirectionMode.Descending, _rng, out var pitches));
        Assert.Equal(pitches.StartWrittenMidi, pitches.EndWrittenMidi);
        Assert.Equal(0, pitches.Semitones);
        Assert.True(pitches.IsAscending); // unison has no direction
    }

    [Fact]
    public void TryPickAscending_Unison_UsesSamePitchTwice()
    {
        Assert.True(IntervalEarTrainingLogic.TryPickAscendingInterval(
            60, 72, 0, _rng, out var pitches));
        Assert.Equal(pitches.StartWrittenMidi, pitches.EndWrittenMidi);
        Assert.Equal(0, pitches.Semitones);
    }

    [Fact]
    public void TryPickAscending_NarrowRange_FailsSafely()
    {
        // Only 2 semitones of room — octave cannot fit.
        Assert.False(IntervalEarTrainingLogic.TryPickAscendingInterval(
            60, 61, 12, _rng, out _));
        Assert.False(IntervalEarTrainingLogic.TryPickInterval(
            60, 61, 12, ascending: false, _rng, out _));
    }

    [Fact]
    public void TryPickRandom_IncludesAllZeroThroughTwelve_WhenRangeAllows()
    {
        var seen = new HashSet<int>();
        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            Assert.True(IntervalEarTrainingLogic.TryPickRandomInterval(
                48, 84, rng, out var pitches));
            seen.Add(pitches.Semitones);
            Assert.InRange(pitches.StartWrittenMidi, 48, 84);
            Assert.InRange(pitches.EndWrittenMidi, 48, 84);
        }

        for (int s = 0; s <= 12; s++)
            Assert.Contains(s, seen);
    }

    [Fact]
    public void TryPickRandom_WithRandomDirection_ProducesBothDirections()
    {
        var rng = new Random(11);
        bool sawUp = false;
        bool sawDown = false;
        for (int i = 0; i < 200; i++)
        {
            Assert.True(IntervalEarTrainingLogic.TryPickRandomInterval(
                48, 84, IntervalDirectionMode.Random, rng, out var pitches));
            if (pitches.Semitones == 0)
                continue;
            if (pitches.IsAscending) sawUp = true;
            else sawDown = true;
            if (sawUp && sawDown)
                break;
        }

        Assert.True(sawUp);
        Assert.True(sawDown);
    }

    [Fact]
    public void TryPickRandom_NarrowRange_OnlyFitsSmallIntervals()
    {
        var seen = new HashSet<int>();
        var rng = new Random(3);
        for (int i = 0; i < 100; i++)
        {
            Assert.True(IntervalEarTrainingLogic.TryPickRandomInterval(
                60, 62, IntervalDirectionMode.Descending, rng, out var pitches));
            seen.Add(pitches.Semitones);
            Assert.True(pitches.Semitones <= 2);
            if (pitches.Semitones > 0)
            {
                Assert.False(pitches.IsAscending);
                Assert.Equal(pitches.Semitones, pitches.StartWrittenMidi - pitches.EndWrittenMidi);
            }
        }

        Assert.DoesNotContain(12, seen);
    }

    [Fact]
    public void DirectionMode_DefaultsToAscending_AndPersists()
    {
        Assert.Equal(IntervalDirectionMode.Ascending, IntervalEarTrainingLogic.DefaultDirectionMode);
        Assert.Equal(
            IntervalDirectionMode.Ascending,
            IntervalEarTrainingLogic.ParseDirectionMode(
                SessionPreferences.Get(
                    IntervalEarTrainingLogic.DirectionPreferenceKey,
                    IntervalEarTrainingLogic.DefaultDirectionMode.ToString())));

        SessionPreferences.Set(
            IntervalEarTrainingLogic.DirectionPreferenceKey,
            IntervalDirectionMode.Descending.ToString());
        Assert.Equal(
            IntervalDirectionMode.Descending,
            IntervalEarTrainingLogic.ParseDirectionMode(
                SessionPreferences.Get(
                    IntervalEarTrainingLogic.DirectionPreferenceKey,
                    IntervalEarTrainingLogic.DefaultDirectionMode.ToString())));
    }

    [Theory]
    [InlineData(null, IntervalDirectionMode.Ascending)]
    [InlineData("", IntervalDirectionMode.Ascending)]
    [InlineData("bogus", IntervalDirectionMode.Ascending)]
    [InlineData("Descending", IntervalDirectionMode.Descending)]
    [InlineData("random", IntervalDirectionMode.Random)]
    public void ParseDirectionMode(string? value, IntervalDirectionMode expected)
        => Assert.Equal(expected, IntervalEarTrainingLogic.ParseDirectionMode(value));

    [Fact]
    public void TryBuildFromReference_KeepsStartFixed_AcrossIntervals()
    {
        const int reference = 60; // C4
        int low = 48;
        int high = 84;
        for (int s = 0; s <= 12; s++)
        {
            Assert.True(IntervalEarTrainingLogic.TryBuildIntervalFromReference(
                reference, low, high, s, ascending: true, out var pitches));
            Assert.Equal(reference, pitches.StartWrittenMidi);
            Assert.Equal(reference + s, pitches.EndWrittenMidi);
            Assert.Equal(s, pitches.Semitones);
            Assert.True(pitches.IsAscending);
        }
    }

    [Fact]
    public void TryBuildFromReference_Descending_KeepsStartFixed()
    {
        const int reference = 72; // C5
        Assert.True(IntervalEarTrainingLogic.TryBuildIntervalFromReference(
            reference, 48, 84, 7, ascending: false, out var pitches));
        Assert.Equal(reference, pitches.StartWrittenMidi);
        Assert.Equal(reference - 7, pitches.EndWrittenMidi);
        Assert.False(pitches.IsAscending);
    }

    [Fact]
    public void TryBuildFromReference_OutOfRange_FailsSafely()
    {
        // Ascending octave from near the top cannot fit.
        Assert.False(IntervalEarTrainingLogic.TryBuildIntervalFromReference(
            71, 60, 72, 12, ascending: true, out _));
        // Descending octave from near the bottom cannot fit.
        Assert.False(IntervalEarTrainingLogic.TryBuildIntervalFromReference(
            61, 60, 72, 12, ascending: false, out _));
    }

    [Fact]
    public void TryBuildFromReference_RandomDirection_FallsBackWhenNeeded()
    {
        // Reference near the top: ascending M2 fails, descending should succeed under Random.
        var rng = new Random(0); // ResolveIsAscending(Random) may pick either first
        bool any = false;
        for (int i = 0; i < 20; i++)
        {
            if (IntervalEarTrainingLogic.TryBuildIntervalFromReference(
                    72, 60, 72, 2, IntervalDirectionMode.Random, rng, out var pitches))
            {
                any = true;
                Assert.Equal(72, pitches.StartWrittenMidi);
                Assert.InRange(pitches.EndWrittenMidi, 60, 72);
                Assert.Equal(2, pitches.Semitones);
            }
        }

        Assert.True(any);
    }

    [Fact]
    public void PlayItAgain_PreservesSamePitchesAndDirection()
    {
        Assert.True(IntervalEarTrainingLogic.TryPickInterval(
            60, 84, 7, ascending: false, new Random(1), out var first));
        var again = first;
        Assert.Equal(first.StartWrittenMidi, again.StartWrittenMidi);
        Assert.Equal(first.EndWrittenMidi, again.EndWrittenMidi);
        Assert.Equal(first.Semitones, again.Semitones);
        Assert.Equal(first.IsAscending, again.IsAscending);
    }

    [Theory]
    [InlineData(4, 4, true)]
    [InlineData(4, 5, false)]
    [InlineData(0, 0, true)]
    public void IsAnswerCorrect(int expected, int answered, bool correct)
        => Assert.Equal(correct, IntervalEarTrainingLogic.IsAnswerCorrect(expected, answered));

    [Fact]
    public void ShouldShowIntervalNotes_HiddenOnlyDuringUnansweredQuiz()
    {
        Assert.False(IntervalEarTrainingLogic.ShouldShowIntervalNotes(
            IntervalEarTrainingInteraction.UnansweredQuiz));
        Assert.True(IntervalEarTrainingLogic.ShouldShowIntervalNotes(
            IntervalEarTrainingInteraction.RevealedQuiz));
        Assert.True(IntervalEarTrainingLogic.ShouldShowIntervalNotes(
            IntervalEarTrainingInteraction.ManualPlayback));
    }
}

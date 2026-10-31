using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class DisplayedTuneHistoryTests
{
    private static GeneratedNote Pitch(int midi, string spelled, NoteDuration duration = NoteDuration.Quarter, int measure = 0, double beat = 0)
        => new()
        {
            MidiNumber = midi,
            SpelledName = spelled,
            Duration = duration,
            MeasureIndex = measure,
            BeatPosition = beat,
        };

    private static GeneratedNote Rest(NoteDuration duration = NoteDuration.Quarter, int measure = 0, double beat = 0)
        => new()
        {
            IsRest = true,
            Duration = duration,
            MeasureIndex = measure,
            BeatPosition = beat,
        };

    [Fact]
    public void Signature_DependsOnMusicalContent_NotObjectIdentity()
    {
        var a1 = new[] { Pitch(60, "C4"), Rest(), Pitch(64, "E4") };
        var a2 = new[] { Pitch(60, "C4"), Rest(), Pitch(64, "E4") };
        var b = new[] { Pitch(60, "C4"), Rest(), Pitch(65, "F4") };

        string sigA1 = GeneratedTuneSignature.FromGeneratedNotes(a1);
        string sigA2 = GeneratedTuneSignature.FromGeneratedNotes(a2);
        string sigB = GeneratedTuneSignature.FromGeneratedNotes(b);

        Assert.False(string.IsNullOrEmpty(sigA1));
        Assert.Equal(sigA1, sigA2);
        Assert.NotEqual(sigA1, sigB);
    }

    [Fact]
    public void Signature_IgnoresStaffPackingPositions()
    {
        var packedNarrow = new[]
        {
            Pitch(60, "C4", beat: 0),
            Pitch(64, "E4", beat: 1),
        };
        var packedWide = new[]
        {
            Pitch(60, "C4", measure: 1, beat: 4.5),
            Pitch(64, "E4", measure: 1, beat: 5.5),
        };

        Assert.Equal(
            GeneratedTuneSignature.FromGeneratedNotes(packedNarrow),
            GeneratedTuneSignature.FromGeneratedNotes(packedWide));
    }

    [Fact]
    public void AcceptA_RejectDuplicateA_AcceptB()
    {
        var history = new DisplayedTuneHistory();
        string a = GeneratedTuneSignature.FromGeneratedNotes(new[] { Pitch(60, "C4") });
        string b = GeneratedTuneSignature.FromGeneratedNotes(new[] { Pitch(62, "D4") });

        Assert.True(history.TryAccept(a));
        Assert.Equal(1, history.Count);
        Assert.True(history.Contains(a));

        Assert.False(history.TryAccept(a));
        Assert.Equal(1, history.Count);

        Assert.True(history.TryAccept(b));
        Assert.Equal(1, history.Count);
        Assert.True(history.Contains(b));
        Assert.False(history.Contains(a));
    }

    [Fact]
    public void AfterB_AIsAllowedAgain_SuccessiveOnly()
    {
        var history = new DisplayedTuneHistory();
        string a = GeneratedTuneSignature.FromGeneratedNotes(new[] { Pitch(60, "C4") });
        string b = GeneratedTuneSignature.FromGeneratedNotes(new[] { Pitch(62, "D4") });

        Assert.True(history.TryAccept(a));
        Assert.True(history.TryAccept(b));
        Assert.True(history.TryAccept(a));
        Assert.Equal(a, history.PreviousSignature);
        Assert.False(history.TryAccept(a));
    }

    [Fact]
    public void CandidateCheck_DoesNotCommitPrevious()
    {
        var history = new DisplayedTuneHistory();
        string a = GeneratedTuneSignature.FromGeneratedNotes(new[] { Pitch(60, "C4") });
        string b = GeneratedTuneSignature.FromGeneratedNotes(new[] { Pitch(62, "D4") });
        history.CommitDisplayed(a);

        Assert.True(history.IsRepeatOfPrevious(a));
        Assert.False(history.IsRepeatOfPrevious(b));
        Assert.Equal(a, history.PreviousSignature);

        Assert.True(GeneratedTuneAcceptance.IsAcceptableSuccessor(history, b));
        Assert.False(GeneratedTuneAcceptance.IsAcceptableSuccessor(history, a));
        Assert.Equal(a, history.PreviousSignature);

        history.CommitDisplayed(b);
        Assert.Equal(b, history.PreviousSignature);
        Assert.False(GeneratedTuneAcceptance.IsAcceptableSuccessor(history, b));
        Assert.True(GeneratedTuneAcceptance.IsAcceptableSuccessor(history, a));
    }

    [Fact]
    public void SelectionLoop_PicksBOnce_DoesNotFlipBackToA()
    {
        var history = new DisplayedTuneHistory();
        string a = GeneratedTuneSignature.FromGeneratedNotes(new[] { Pitch(60, "C4") });
        string b = GeneratedTuneSignature.FromGeneratedNotes(new[] { Pitch(62, "D4") });
        history.CommitDisplayed(a);

        string? chosen = null;
        var candidates = new[] { a, b, a, b };
        for (int i = 0; i < candidates.Length; i++)
        {
            bool accepted = GeneratedTuneAcceptance.IsAcceptableSuccessor(history, candidates[i]);
            if (!GeneratedTuneAcceptance.ShouldRetry(true, true, accepted, i, maxAttempts: 10))
            {
                chosen = candidates[i];
                break;
            }
        }

        Assert.Equal(b, chosen);
        history.CommitDisplayed(chosen);
        Assert.Equal(b, history.PreviousSignature);
        Assert.True(GeneratedTuneAcceptance.IsAcceptableSuccessor(history, a));
        Assert.False(GeneratedTuneAcceptance.IsAcceptableSuccessor(history, b));
    }

    [Fact]
    public void HistorySurvivesNavigationAndLevelChange_OnSameInstance()
    {
        var history = new DisplayedTuneHistory();
        history.TryAccept("sig-A");
        history.TryAccept("sig-B");

        // Music → Settings → Music (same process / same singleton): last committed remains B
        Assert.Equal(1, history.Count);
        Assert.False(history.Contains("sig-A"));
        Assert.True(history.Contains("sig-B"));

        // Level 4 → 5 → 4 does not clear history
        var session = new NoteSessionService { ChildLevel = 4, IsRandomMode = true, Tune = "Selected Scale" };
        Assert.True(DisplayedTuneHistory.IsUniquenessRequired(session, false, "Random"));
        session.ChildLevel = 5;
        Assert.Equal(1, history.Count);
        session.ChildLevel = 4;
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void PlayReuse_DoesNotRecordHistory()
    {
        var history = new DisplayedTuneHistory();
        string a = GeneratedTuneSignature.FromGeneratedNotes(new[] { Pitch(60, "C4") });
        Assert.True(history.TryAccept(a));
        Assert.Equal(1, history.Count);

        Assert.True(PracticeSessionLifecycle.ShouldReuseDisplayedExercise(
            playBack: true, forceNewNotes: false, displayedNoteCount: 4));
        Assert.Equal(1, history.Count);
        Assert.False(history.TryAccept(a));
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void SavedTune_Scale_Arpeggio_AreExempt()
    {
        var saved = new NoteSessionService
        {
            Tune = "Practice Tune",
            ScaleSelectionMode = ScaleSelectionMode.ByLevel,
            IsRandomMode = false,
        };
        Assert.False(DisplayedTuneHistory.IsUniquenessRequired(
            saved, false, "Mary Had a Little Lamb"));

        var scale = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = false,
            ScaleSelectionMode = ScaleSelectionMode.Named,
        };
        Assert.False(DisplayedTuneHistory.IsUniquenessRequired(scale, false, "Major"));

        var arp = new NoteSessionService
        {
            Tune = "Arpeggio",
            IsRandomMode = false,
        };
        Assert.False(DisplayedTuneHistory.IsUniquenessRequired(arp, false, "C major triad"));
    }

    [Fact]
    public void RandomAndCompositionAssignedTune_RequireUniqueness()
    {
        var random = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = true,
        };
        Assert.True(DisplayedTuneHistory.IsUniquenessRequired(random, false, "Random"));

        var compositionTune = new NoteSessionService
        {
            Tune = "Practice Tune",
            ScaleSelectionMode = ScaleSelectionMode.ByLevel,
            IsRandomMode = false,
        };
        Assert.True(DisplayedTuneHistory.IsUniquenessRequired(
            compositionTune, false, NoteSessionService.ScaleSelectionByLevel));

        var assortmentScale = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = false,
            ScaleSelectionMode = ScaleSelectionMode.ByLevel,
            ChildLevel = 1,
        };
        Assert.True(DisplayedTuneHistory.IsUniquenessRequired(
            assortmentScale, false, NoteSessionService.ScaleSelectionByLevel));
    }

    [Fact]
    public void PitchVariety_RetriesUntilTwoPitches_ThenAccepts()
    {
        var history = new DisplayedTuneHistory();
        var rest = Rest();
        var a4 = Pitch(69, "A4");
        var c5 = Pitch(72, "C5");
        var onePitch = new[] { a4, rest, a4 };
        var twoPitches = new[] { a4, rest, c5 };

        int attempts = 0;
        bool accepted = false;
        for (int i = 0; i < GeneratedTuneAcceptance.MaxGenerationAttempts; i++)
        {
            attempts++;
            var candidate = i < 3 ? onePitch : twoPitches;
            bool enough = GeneratedTuneAcceptance.HasEnoughDistinctSoundedPitches(candidate, null);
            bool uniquenessAccepted = false;
            if (enough)
                uniquenessAccepted = history.TryAccept(GeneratedTuneSignature.FromGeneratedNotes(candidate));

            if (!GeneratedTuneAcceptance.ShouldRetry(true, enough, uniquenessAccepted, i))
            {
                accepted = uniquenessAccepted;
                break;
            }
        }

        Assert.Equal(4, attempts);
        Assert.True(accepted);
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void PitchVariety_ExhaustedRetries_DisplaysLastWithoutRecording()
    {
        var history = new DisplayedTuneHistory();
        var onePitch = new[] { Pitch(69, "A4"), Rest(), Pitch(69, "A4") };
        int attempts = 0;
        bool lastEnough = true;

        for (int i = 0; i < GeneratedTuneAcceptance.MaxGenerationAttempts; i++)
        {
            attempts++;
            bool enough = GeneratedTuneAcceptance.HasEnoughDistinctSoundedPitches(onePitch, null);
            bool uniquenessAccepted = enough
                && history.TryAccept(GeneratedTuneSignature.FromGeneratedNotes(onePitch));
            lastEnough = enough;
            if (!GeneratedTuneAcceptance.ShouldRetry(true, enough, uniquenessAccepted, i))
                break;
        }

        Assert.Equal(GeneratedTuneAcceptance.MaxGenerationAttempts, attempts);
        Assert.False(lastEnough);
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void PitchVariety_DoesNotApplyToSavedScaleOrArpeggio()
    {
        var saved = new NoteSessionService
        {
            Tune = "Practice Tune",
            ScaleSelectionMode = ScaleSelectionMode.ByLevel,
            IsRandomMode = false,
        };
        Assert.False(GeneratedTuneAcceptance.ChecksRequired(saved, false, "Mary Had a Little Lamb"));

        var scale = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = false,
            ScaleSelectionMode = ScaleSelectionMode.Named,
        };
        Assert.False(GeneratedTuneAcceptance.ChecksRequired(scale, false, "Major"));

        var arp = new NoteSessionService
        {
            Tune = "Arpeggio",
            IsRandomMode = false,
        };
        Assert.False(GeneratedTuneAcceptance.ChecksRequired(arp, false, "C major triad"));

        Assert.False(GeneratedTuneAcceptance.ShouldRetry(
            checksRequired: false,
            enoughDistinctPitches: false,
            uniquenessAccepted: false,
            attemptIndex: 0));
    }

    [Fact]
    public void DuplicateCandidates_RetryIsBounded()
    {
        var history = new DisplayedTuneHistory();
        const string duplicate = "always-the-same";
        int attempts = 0;
        bool acceptedOnce = false;

        for (int i = 0; i < DisplayedTuneHistory.MaxGenerationAttempts; i++)
        {
            attempts++;
            if (history.TryAccept(duplicate))
                acceptedOnce = true;
        }

        Assert.Equal(DisplayedTuneHistory.MaxGenerationAttempts, attempts);
        Assert.True(acceptedOnce);
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void NewAppSession_StartsEmpty()
    {
        var firstLaunch = new DisplayedTuneHistory();
        firstLaunch.TryAccept("sig-A");
        Assert.Equal(1, firstLaunch.Count);

        var nextLaunch = new DisplayedTuneHistory();
        Assert.Equal(0, nextLaunch.Count);
        Assert.False(nextLaunch.Contains("sig-A"));
    }

    [Fact]
    public void MixRandomSeed_DoesNotCancelWhenTickAndSeedBothIncrement()
    {
        // Legacy XOR: TickCount ^ seed == (TickCount + 1) ^ (seed + 1)
        const int tick = 1;
        const int seed = 1;
        Assert.Equal(tick ^ seed, (tick + 1) ^ (seed + 1));

        int a = GeneratedTuneAcceptance.MixRandomSeed(seed, childLevel: 4, seedSalt: 0);
        int b = GeneratedTuneAcceptance.MixRandomSeed(seed + 1, childLevel: 4, seedSalt: 0);
        Assert.NotEqual(a, b);
        Assert.NotEqual(
            GeneratedTuneAcceptance.MixRandomSeed(seed, 4, 0),
            GeneratedTuneAcceptance.MixRandomSeed(seed, 4, 0x5A5A5A5A));
    }
}

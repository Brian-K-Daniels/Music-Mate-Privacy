using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// B♭ clarinet sounds a major second below the written note.
/// Expected and heard pitches stay labeled as written or concert, and the
/// microphone frequency is compared with the expected concert pitch once.
/// </summary>
[Collection("SessionPreferences")]
public class BbClarinetPitchDomainTests : IDisposable
{
    public BbClarinetPitchDomainTests()
    {
        SessionPreferences.TestStore = new Dictionary<string, object?>();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Theory]
    [InlineData("F4", 65, 63, "Eb4")]
    [InlineData("Ab4", 68, 66, "Gb4")]
    [InlineData("Bb3", 58, 56, "Ab3")]
    [InlineData("Gb4", 66, 64, "E4")]
    [InlineData("F#4", 66, 64, "E4")]
    [InlineData("E4", 64, 62, "D4")]
    [InlineData("C#5", 73, 71, "B4")]
    [InlineData("C4", 60, 58, "Bb3")]
    [InlineData("C3", 48, 46, "Bb2")]
    [InlineData("C5", 72, 70, "Bb4")]
    [InlineData("B3", 59, 57, "A3")]
    [InlineData("Cb5", 71, 69, "A4")]
    public void WrittenNote_TransposesOnce_ToConcert(
        string written, int writtenMidi, int concertMidi, string concertName)
    {
        var session = CreateClarinet();
        var note = new NoteInfo { Name = written, Midi = writtenMidi, TargetFreq = Hz(writtenMidi) };
        var domains = session.DescribePitchDomains(note, Hz(concertMidi));

        Assert.Equal(written, domains.ExpectedWrittenPitch);
        Assert.Equal(writtenMidi, domains.ExpectedWrittenMidi);
        Assert.Equal(concertMidi, domains.ExpectedConcertMidi);
        Assert.Equal(concertName, domains.ExpectedConcertPitch);
        Assert.Equal(session.InstrumentTransposeOffset, domains.ExpectedConcertMidi - domains.ExpectedWrittenMidi);
        Assert.NotEqual(session.InstrumentTransposeOffset * 2, domains.ExpectedConcertMidi - domains.ExpectedWrittenMidi);
        Assert.Equal(concertMidi, domains.HeardConcertMidi);
        Assert.Equal(concertMidi, NoteSessionService.FrequencyToConcertMidi(Hz(concertMidi)));
        Assert.Equal(writtenMidi, domains.HeardWrittenMidi);
        Assert.Equal(written, domains.HeardWrittenPitch);
    }

    [Fact]
    public void KeySignature_IsAppliedBeforeClarinetTransposition()
    {
        var bareA = new GeneratedNote
        {
            MidiNumber = 69,
            Letter = 'A',
            Octave = 4,
            SpelledName = "A4",
            Accidental = Accidental.None,
        };

        var (writtenMidi, writtenName) = NoteSessionService.ResolveTargetPitch(bareA, "Db", "Major");
        Assert.Equal("Ab4", writtenName);
        Assert.Equal(68, writtenMidi);

        var session = CreateClarinet();
        session.RestoreRepeatSameGenerationContext(
            "Db", "Major", "Major", ScaleSelectionMode.Named, false, "Selected Scale");

        // Name still says A4, but the stored MIDI already includes the D♭ signature.
        // Evaluation must read A♭ before transposing, not concert-transpose A natural.
        var unresolved = new NoteInfo { Name = "A4", Midi = writtenMidi, TargetFreq = Hz(writtenMidi) };
        var domains = session.DescribePitchDomains(unresolved, Hz(66));

        Assert.Equal("Ab4", domains.ExpectedWrittenPitch);
        Assert.Equal(68, domains.ExpectedWrittenMidi);
        Assert.Equal(66, domains.ExpectedConcertMidi);
        Assert.Equal("Gb4", domains.ExpectedConcertPitch);

        session.NotesToDraw.Add(unresolved);
        session.FeedbackViewModels.Add(new FeedbackItem(0, 0, 0, false));
        var (match, within, cents) = session.EvaluatePitchMatch(Hz(66));
        Assert.True(match);
        Assert.True(within);
        Assert.InRange(cents, -5, 5);

        var (concertAbMatch, _, _) = session.EvaluatePitchMatch(Hz(68));
        Assert.False(concertAbMatch);
    }

    [Fact]
    public void ExplicitNatural_IsNotFlattedByTheKeySignature()
    {
        var naturalA = new GeneratedNote
        {
            MidiNumber = 69,
            Letter = 'A',
            Octave = 4,
            SpelledName = "A4",
            Accidental = Accidental.Natural,
        };

        var (writtenMidi, writtenName) = NoteSessionService.ResolveTargetPitch(naturalA, "Db", "Major");
        Assert.Equal("A4", writtenName);
        Assert.Equal(69, writtenMidi);

        var session = CreateClarinet();
        session.Key = "Db";
        var domains = session.DescribePitchDomains(
            new NoteInfo { Name = writtenName, Midi = writtenMidi }, Hz(67));

        Assert.Equal("A4", domains.ExpectedWrittenPitch);
        Assert.Equal(67, domains.ExpectedConcertMidi);
        Assert.Equal("G4", domains.ExpectedConcertPitch);
    }

    [Fact]
    public void MicrophoneConcertPitch_IsComparedWithExpectedConcert_NotWrittenFrequency()
    {
        var session = CreateClarinet();
        session.NotesToDraw.Add(new NoteInfo
        {
            Name = "Ab4",
            Midi = 68,
            // Trap: stored frequency is the written pitch. Grading must not use it.
            TargetFreq = Hz(68),
        });
        session.FeedbackViewModels.Add(new FeedbackItem(0, 0, 0, false));

        var (correct, cents) = session.Evaluate(Hz(66));
        Assert.True(correct);
        Assert.InRange(cents, -5, 5);

        Assert.False(session.Evaluate(Hz(68)).correct);
        Assert.False(session.Evaluate(Hz(64)).correct);

        var played = session.DescribePitchDomains(session.NotesToDraw[0], Hz(66));
        Assert.Equal(
            "Expected: written Ab4 / concert Gb4 | Heard: concert Gb4 / written Ab4, 0¢, Notes: 1",
            NoteSessionService.FormatPitchDiagnostic(played, 0, 1));

        var concertAb = session.DescribePitchDomains(session.NotesToDraw[0], Hz(68));
        // The same letters are different pitches: written A♭4 sounds as concert G♭4.
        // A microphone on concert A♭4 is written B♭4 for this clarinet.
        Assert.Equal("Ab4", concertAb.ExpectedWrittenPitch);
        Assert.Equal("Gb4", concertAb.ExpectedConcertPitch);
        Assert.Equal("Ab4", concertAb.HeardConcertPitch);
        Assert.Equal("Bb4", concertAb.HeardWrittenPitch);
        Assert.Equal(68, concertAb.ExpectedWrittenMidi);
        Assert.Equal(66, concertAb.ExpectedConcertMidi);
        Assert.Equal(68, concertAb.HeardConcertMidi);
        Assert.Equal(70, concertAb.HeardWrittenMidi);
    }

    [Fact]
    public void Screenshot_EbKey_BlueAIsAb_AndExpectedLeavesGreenF()
    {
        // Three flats. The green note is F (1st space). The blue note is A
        // (2nd space), which that signature makes A♭. The old status said
        // Expected: F4, Heard: Ab4, 334¢ while the player was already on the blue note.
        var session = CreateClarinet();
        session.RestoreRepeatSameGenerationContext(
            "Eb", "Major", "Major", ScaleSelectionMode.Named, false, "Selected Scale");

        var staffA = new GeneratedNote
        {
            MidiNumber = 69,
            Letter = 'A',
            Octave = 4,
            SpelledName = "A4",
            Accidental = Accidental.None,
        };
        Assert.Equal("Ab4", session.WrittenNameForStaffLabel(staffA));

        double elapsed = 0;
        session.CooldownMs = 0;
        session.WrongDebounceMs = 0;
        session.Tempo = 119;
        session.ShowConductorCues = false;
        session.ChildLevel = 1;
        session.SessionElapsedMsOverride = () => elapsed;
        AddNote(session, "F4", 65, startBeat: 0);
        AddNote(session, "Ab4", 68, startBeat: 1);
        session.ConfigureRhythmStartGates();
        session.StartListeningClock();

        // Concert G♭ is written A♭, about a minor third above written F's concert E♭.
        // 34 cents sharp of that G♭ is the 334¢ the screenshot showed against F.
        double heardHz = Hz(66) * Math.Pow(2, 34 / 1200.0);
        var againstF = session.DescribePitchDomains(session.NotesToDraw[0], heardHz);
        Assert.Equal("F4", againstF.ExpectedWrittenPitch);
        Assert.Equal("Eb4", againstF.ExpectedConcertPitch);
        Assert.Equal("Ab4", againstF.HeardWrittenPitch);
        int centsAgainstF = NoteSessionService.CentsFromExpectedConcert(heardHz, againstF.ExpectedConcertMidi);
        Assert.InRange(centsAgainstF, 320, 350);
        session.UpdateFeedbackForCurrent(heardHz, session.Evaluate(heardHz));
        Assert.Equal(0, session.CurrentNoteIndex);
        Assert.DoesNotContain(0, session.CorrectNoteIndices);
        Assert.Contains("Expected: written F4 / concert Eb4", StatusService.Instance.StatusMessage);

        Assert.True(session.UpdateFeedbackForCurrent(Hz(63), session.Evaluate(Hz(63))));
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Equal(StaffNoteState.Correct, StaffNoteStateResolver.Resolve(
            0, session.CurrentNoteIndex, true, session.CorrectNoteIndices, session.NoteFeedbacks));
        Assert.Equal(StaffNoteState.Current, StaffNoteStateResolver.Resolve(
            1, session.CurrentNoteIndex, true, session.CorrectNoteIndices, session.NoteFeedbacks));

        var againstAb = session.DescribePitchDomains(session.NotesToDraw[1], heardHz);
        Assert.Equal("Ab4", againstAb.ExpectedWrittenPitch);
        Assert.Equal("Gb4", againstAb.ExpectedConcertPitch);
        int centsAgainstAb = NoteSessionService.CentsFromExpectedConcert(heardHz, againstAb.ExpectedConcertMidi);
        Assert.InRange(centsAgainstAb, 20, 50);
        Assert.Contains("Expected: written Ab4 / concert Gb4", StatusService.Instance.StatusMessage);
        Assert.DoesNotContain("Expected: written F4", StatusService.Instance.StatusMessage);
    }

    [Fact]
    public void AcceptedNote_AdvancesBlueNoteAndExpectedTogether()
    {
        double elapsed = 0;
        var session = CreateClarinet();
        session.Tune = "Selected Scale";
        session.Key = "F";
        session.CooldownMs = 0;
        session.WrongDebounceMs = 0;
        session.Tempo = 60;
        session.ShowConductorCues = false;
        session.ChildLevel = 1;
        session.SessionElapsedMsOverride = () => elapsed;
        AddNote(session, "F4", 65, startBeat: 0);
        AddNote(session, "Ab4", 68, startBeat: 1);
        session.ConfigureRhythmStartGates();
        session.StartListeningClock();

        // The player is already fingering the next written note (A♭ → concert G♭).
        // That must not satisfy written F4, and Expected must stay on F4.
        double nextConcert = Hz(66);
        Assert.False(session.UpdateFeedbackForCurrent(nextConcert, session.Evaluate(nextConcert)));
        Assert.Equal(0, session.CurrentNoteIndex);
        Assert.DoesNotContain(0, session.CorrectNoteIndices);
        Assert.Contains("Expected: written F4 / concert Eb4", StatusService.Instance.StatusMessage);
        Assert.Contains("Heard: concert Gb4 / written Ab4", StatusService.Instance.StatusMessage);

        double currentConcert = Hz(63);
        Assert.True(session.UpdateFeedbackForCurrent(currentConcert, session.Evaluate(currentConcert)));

        Assert.Contains(0, session.CorrectNoteIndices);
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Equal(StaffNoteState.Correct, StaffNoteStateResolver.Resolve(
            0, session.CurrentNoteIndex, true, session.CorrectNoteIndices, session.NoteFeedbacks));
        Assert.Equal(StaffNoteState.Current, StaffNoteStateResolver.Resolve(
            1, session.CurrentNoteIndex, true, session.CorrectNoteIndices, session.NoteFeedbacks));

        var advanced = session.DescribePitchDomains(session.NotesToDraw[session.CurrentNoteIndex], currentConcert);
        Assert.Equal("Ab4", advanced.ExpectedWrittenPitch);
        Assert.Equal(66, advanced.ExpectedConcertMidi);
        Assert.Contains("Expected: written Ab4 / concert Gb4", StatusService.Instance.StatusMessage);
        Assert.DoesNotContain("Expected: written F4", StatusService.Instance.StatusMessage);
    }

    private static NoteSessionService CreateClarinet()
    {
        var session = new NoteSessionService
        {
            Instrument = "bb-clarinet",
            Tolerance = 15,
            Tune = "Selected Scale",
        };
        Assert.Equal(-2, session.InstrumentTransposeOffset);
        return session;
    }

    private static void AddNote(NoteSessionService session, string written, int writtenMidi, double startBeat)
    {
        int index = session.NotesToDraw.Count;
        session.NotesToDraw.Add(new NoteInfo
        {
            Name = written,
            Midi = writtenMidi,
            TargetFreq = Hz(writtenMidi),
            Duration = NoteDuration.Quarter,
            DurationBeats = 1,
            StartBeat = startBeat,
            GateBeatsAfterPrevious = index == 0 ? 0 : 1,
        });
        session.FeedbackViewModels.Add(new FeedbackItem(index, 0, 0, false));
    }

    private static double Hz(int midi) => NoteSessionService.MidiToFreqPublic(midi);
}

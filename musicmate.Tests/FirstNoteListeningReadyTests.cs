using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// The opening note of a tune is not graded until the microphone has delivered
/// one pitch-analysis window. A leading rest, a sharp or flat, AutoStart, and
/// B♭ clarinet transposition do not change that, and they do not drop the note.
/// </summary>
[Collection("SessionPreferences")]
public class FirstNoteListeningReadyTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public FirstNoteListeningReadyTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void SavedTune_FirstNote_IsAcceptedOnlyAfterListeningIsReady()
    {
        // Saved Tune 2: quarter rest, then written F#5. The rest is not a pitch target.
        var session = CreateSession("bb-clarinet", "Practice Tune");
        AddPitchedNote(session, "F#5", writtenMidi: 78, startBeat: 1.0, NoteDuration.Eighth);
        session.ConfigureRhythmStartGates();

        var gate = new FirstNoteListeningReadiness();
        gate.BeginSession(session.PitchWindowSize);
        gate.MarkCaptureRunning();

        double sounding = Hz(76); // written F#5 on B♭ clarinet sounds E5
        Assert.False(gate.MayGradeMusicPitch(isTuner: false));
        GradeIfReady(session, gate, sounding);
        Assert.Equal(0, session.CurrentNoteIndex);
        Assert.False(session.IsListeningClockRunning);
        Assert.DoesNotContain(0, session.CorrectNoteIndices);

        Assert.True(FeedUntilArmed(gate, session.PitchWindowSize));
        Assert.True(gate.ShouldGradePitch);
        Assert.True(gate.AudioBuffersReceived > 0);

        GradeIfReady(session, gate, sounding);

        Assert.True(session.IsListeningClockRunning);
        Assert.Contains(0, session.CorrectNoteIndices);
        Assert.Equal(1, session.CurrentNoteIndex);

        var domains = session.DescribePitchDomains(session.NotesToDraw[0], sounding);
        var diag = FirstNotePitchDiagnostic.FromDomains(
            0, domains, gate.ListeningActive, gate.AudioBuffersReceived,
            pitchConfidence: 0.8f, rejectionReason: "accepted");
        string line = diag.Format();
        Assert.Contains("index=0", line);
        Assert.Contains("expectedWritten=F#5", line);
        Assert.Contains("expectedConcert=E5", line);
        Assert.Contains("heardConcert=E5", line);
        Assert.Contains("heardWritten=F#5", line);
        Assert.Contains("listening=yes", line);
        Assert.Contains("reason=accepted", line);
        Assert.Equal("F#5", domains.ExpectedWrittenPitch);
        Assert.Equal(78, domains.ExpectedWrittenMidi);
        Assert.Equal("E5", domains.ExpectedConcertPitch);
        Assert.Equal(76, domains.ExpectedConcertMidi);
    }

    [Fact]
    public void FirstNote_SharpOrFlat_IsIncludedInTheExpectedPitch()
    {
        var sharp = new GeneratedNote
        {
            SpelledName = "F#5",
            Letter = 'F',
            Octave = 5,
            Accidental = Accidental.Sharp,
            MidiNumber = 77,
        };
        var (sharpMidi, sharpName) = NoteSessionService.ResolveTargetPitch(sharp, "C", "Major");
        Assert.Equal(78, sharpMidi);
        Assert.Equal("F#5", sharpName);

        var flat = new GeneratedNote
        {
            SpelledName = "Bb4",
            Letter = 'B',
            Octave = 4,
            Accidental = Accidental.Flat,
            MidiNumber = 71,
        };
        var (flatMidi, flatName) = NoteSessionService.ResolveTargetPitch(flat, "C", "Major");
        Assert.Equal(70, flatMidi);
        Assert.Equal("Bb4", flatName);

        var session = CreateSession("bb-clarinet", "Practice Tune");
        AddPitchedNote(session, flatName, flatMidi, startBeat: 0, NoteDuration.Quarter);
        var gate = ArmedGate(session.PitchWindowSize);
        GradeIfReady(session, gate, Hz(68));
        Assert.Contains(0, session.CorrectNoteIndices);

        var domains = session.DescribePitchDomains(session.NotesToDraw[0], Hz(68));
        Assert.Equal("Bb4", domains.ExpectedWrittenPitch);
        Assert.Equal("Ab4", domains.ExpectedConcertPitch);
        Assert.Equal(68, domains.ExpectedConcertMidi);
    }

    [Fact]
    public void OpeningMusicPage_DoesNotGradeTheFirstNote_UntilThePitchWindowIsReady()
    {
        var session = CreateSession("bb-clarinet", "Practice Tune");
        session.AutoStart = true;
        AddPitchedNote(session, "F#5", 78, startBeat: 1.0, NoteDuration.Eighth);

        var gate = new FirstNoteListeningReadiness();
        gate.BeginSession(session.PitchWindowSize);

        Assert.False(FirstNoteListeningReadiness.ShouldPaintCurrentNote(
            showCurrentNoteHighlight: false, gradingArmed: gate.ShouldGradePitch, listeningSession: true));
        Assert.False(gate.MayGradeMusicPitch(isTuner: false));

        GradeIfReady(session, gate, Hz(76));
        Assert.Equal(0, session.CurrentNoteIndex);
        Assert.False(session.IsListeningClockRunning);

        gate.MarkCaptureRunning();
        Assert.False(gate.OnAudioBuffer(1024));
        GradeIfReady(session, gate, Hz(76));
        Assert.Equal(0, session.CurrentNoteIndex);

        Assert.True(FeedUntilArmed(gate, session.PitchWindowSize));
        Assert.True(FirstNoteListeningReadiness.ShouldPaintCurrentNote(
            showCurrentNoteHighlight: true, gradingArmed: true, listeningSession: true));
        GradeIfReady(session, gate, Hz(76));
        Assert.Contains(0, session.CorrectNoteIndices);
    }

    [Fact]
    public void StopThenGo_DoesNotGradeUntilListeningIsReadyAgain()
    {
        var session = CreateSession("concert-pitch", "Practice Tune");
        AddPitchedNote(session, "F#5", 78, startBeat: 0, NoteDuration.Quarter);
        var gate = ArmedGate(session.PitchWindowSize);
        Assert.True(gate.ShouldGradePitch);

        gate.BeginSession(session.PitchWindowSize);
        gate.MarkCaptureStopped();
        Assert.Equal(0, gate.AudioBuffersReceived);
        Assert.False(gate.MayGradeMusicPitch(isTuner: false));

        GradeIfReady(session, gate, Hz(78));
        Assert.Equal(0, session.CurrentNoteIndex);
        Assert.False(session.IsListeningClockRunning);

        gate.MarkCaptureRunning();
        Assert.True(FeedUntilArmed(gate, session.PitchWindowSize));
        GradeIfReady(session, gate, Hz(78));
        Assert.Contains(0, session.CorrectNoteIndices);
        Assert.Equal(1, session.CurrentNoteIndex);

        var domains = session.DescribePitchDomains(session.NotesToDraw[0], Hz(78));
        Assert.Equal("F#5", domains.ExpectedWrittenPitch);
        Assert.Equal("F#5", domains.ExpectedConcertPitch);
        Assert.Equal(78, domains.ExpectedConcertMidi);
    }

    [Fact]
    public void ListeningStartsWhenMusicAppears_StillWaitsForThePitchWindow()
    {
        var session = CreateSession("bb-clarinet", "Practice Tune");
        session.AutoStart = true;
        Assert.True(session.AutoStart);
        AddPitchedNote(session, "F#5", 78, startBeat: 1.0, NoteDuration.Eighth);

        var gate = new FirstNoteListeningReadiness();
        gate.BeginSession(4096);
        Assert.False(gate.ShouldGradePitch);

        GradeIfReady(session, gate, Hz(76));
        Assert.DoesNotContain(0, session.CorrectNoteIndices);

        gate.MarkCaptureRunning();
        Assert.True(FeedUntilArmed(gate, 4096));
        GradeIfReady(session, gate, Hz(76));
        Assert.Contains(0, session.CorrectNoteIndices);
        Assert.True(session.IsListeningClockRunning);
    }

    [Fact]
    public void BbClarinet_FirstNote_TransposesTheExpectedPitchExactlyOnce()
    {
        var session = CreateSession("bb-clarinet", "Practice Tune");
        Assert.Equal(-2, session.InstrumentTransposeOffset);
        AddPitchedNote(session, "F#5", 78, startBeat: 1.0, NoteDuration.Eighth);
        session.NotesToDraw[0].TargetFreq = Hz(78);

        var gate = ArmedGate(session.PitchWindowSize);

        GradeIfReady(session, gate, Hz(78));
        Assert.Equal(0, session.CurrentNoteIndex);
        Assert.False(session.IsListeningClockRunning);
        var skipped = session.DescribePitchDomains(session.NotesToDraw[0], Hz(78));
        Assert.Equal("F#5", skipped.ExpectedWrittenPitch);
        Assert.Equal("E5", skipped.ExpectedConcertPitch);
        Assert.Equal("F#5", skipped.HeardConcertPitch);
        Assert.NotEqual(skipped.ExpectedConcertMidi, skipped.HeardConcertMidi);

        GradeIfReady(session, gate, Hz(74));
        Assert.Equal(0, session.CurrentNoteIndex);

        GradeIfReady(session, gate, Hz(76));
        Assert.Contains(0, session.CorrectNoteIndices);
        var matched = session.DescribePitchDomains(session.NotesToDraw[0], Hz(76));
        Assert.Equal(76, matched.ExpectedConcertMidi);
        Assert.Equal(76, matched.HeardConcertMidi);
        Assert.Equal(78, matched.HeardWrittenMidi);
        Assert.Equal("F#5", matched.HeardWrittenPitch);
    }

    private static void GradeIfReady(NoteSessionService session, FirstNoteListeningReadiness gate, double freq)
    {
        if (!gate.MayGradeMusicPitch(isTuner: false))
            return;

        var result = session.Evaluate(freq);
        if (!session.TryArmListeningClockOnFirstCorrectPitch(result.correct))
            return;

        session.UpdateFeedbackForCurrent(freq, result);
    }

    private static bool FeedUntilArmed(FirstNoteListeningReadiness gate, int windowSamples)
    {
        int guard = 0;
        while (!gate.ShouldGradePitch && guard++ < 64)
            gate.OnAudioBuffer(Math.Max(1, windowSamples / 4));
        return gate.ShouldGradePitch;
    }

    private static FirstNoteListeningReadiness ArmedGate(int windowSamples)
    {
        var gate = new FirstNoteListeningReadiness();
        gate.BeginSession(windowSamples);
        gate.MarkCaptureRunning();
        Assert.True(FeedUntilArmed(gate, windowSamples));
        return gate;
    }

    private static NoteSessionService CreateSession(string instrument, string tune)
    {
        var session = new NoteSessionService
        {
            Instrument = instrument,
            Tolerance = 15,
            Tempo = 114,
            Tune = tune,
        };
        return session;
    }

    private static void AddPitchedNote(
        NoteSessionService session, string written, int writtenMidi, double startBeat, NoteDuration duration)
    {
        int index = session.NotesToDraw.Count;
        session.NotesToDraw.Add(new NoteInfo
        {
            Name = written,
            Midi = writtenMidi,
            TargetFreq = Hz(writtenMidi),
            Duration = duration,
            DurationBeats = duration == NoteDuration.Eighth ? 0.5 : 1,
            StartBeat = startBeat,
            GateBeatsAfterPrevious = 0,
        });
        session.FeedbackViewModels.Add(new FeedbackItem(index, 0, 0, false));
    }

    private static double Hz(int midi) => NoteSessionService.MidiToFreqPublic(midi);
}

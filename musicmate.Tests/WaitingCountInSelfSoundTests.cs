using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// App-generated Count-In tones must never score/advance the first expected note.
/// </summary>
[Collection("SessionPreferences")]
public class WaitingCountInSelfSoundTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public WaitingCountInSelfSoundTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Theory]
    [InlineData(4096, 44100, 140)]
    [InlineData(2048, 44100, 70)]
    public void ComputeSelfSoundGuardMs_MatchesWindowPlusHop(
        int windowSize, int sampleRate, int expectedMs)
        => Assert.Equal(
            expectedMs,
            WaitingCountInLogic.ComputeSelfSoundGuardMs(windowSize, sampleRate));

    [Fact]
    public void SuppressMs_IncludesClickDurationPlusGuard()
    {
        int guard = WaitingCountInLogic.ComputeSelfSoundGuardMs(4096, 44100);
        Assert.Equal(
            200 + guard,
            WaitingCountInLogic.ComputeSelfSoundSuppressMs(200, 4096, 44100));
    }

    [Theory]
    [InlineData(true, true, 0, true, false)]   // suppress + no heardHz → treat as unsafe / reject
    [InlineData(true, true, 0, false, true)]  // outside suppress → accept
    [InlineData(false, true, 0, false, false)]
    [InlineData(true, false, 0, false, false)]
    [InlineData(true, true, 1, false, false)]
    public void ShouldAcceptFirstNoteToEndCountIn_RespectsSelfSoundSuppress(
        bool evaluateCorrect,
        bool countInActive,
        int noteIndex,
        bool suppress,
        bool expected)
        => Assert.Equal(
            expected,
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                evaluateCorrect, countInActive, noteIndex, suppress));

    [Fact]
    public void ShouldAccept_DuringSuppress_WhenHeardFarFromClick()
    {
        // C4 during suppress must end Count-In — players articulate on the beat.
        Assert.True(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                evaluateCorrect: true,
                countInActive: true,
                currentNoteIndex: 0,
                withinSelfSoundSuppressWindow: true,
                heardHz: 261.63,
                accentedClickHz: 1760,
                unaccentedClickHz: 880));
    }

    [Fact]
    public void ShouldAccept_DuringSuppress_RejectsNearClickFrequency()
    {
        Assert.False(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                evaluateCorrect: true,
                countInActive: true,
                currentNoteIndex: 0,
                withinSelfSoundSuppressWindow: true,
                heardHz: 1760,
                accentedClickHz: 1760,
                unaccentedClickHz: 880));
        // Octave of click also blocked
        Assert.False(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                evaluateCorrect: true,
                countInActive: true,
                currentNoteIndex: 0,
                withinSelfSoundSuppressWindow: true,
                heardHz: 880,
                accentedClickHz: 1760,
                unaccentedClickHz: 880));
    }

    [Fact]
    public void MatchingPitch_DuringCountInSuppress_AdvancesWhenFarFromClick()
    {
        var session = CreateCMajorSession();
        session.StartListeningClock();
        session.SuppressCountInClickSelfSound(clickDurationMs: 100);
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double firstFreq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(firstFreq);
        Assert.True(result.correct);

        Assert.True(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct,
                true,
                0,
                session.ShouldIgnoreAudio(DateTime.UtcNow),
                heardHz: firstFreq,
                accentedClickHz: WaitingCountInSettings.DefaultAccentedPitchHz,
                unaccentedClickHz: WaitingCountInSettings.DefaultUnaccentedPitchHz));
    }

    [Fact]
    public void MatchingPitch_InResidualGuardAfterClick_AdvancesWhenFarFromClick()
    {
        var session = CreateCMajorSession();
        session.StartListeningClock();
        // Duration 0 → residual guard only (post-click window flush).
        session.SuppressCountInClickSelfSound(clickDurationMs: 0);
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(freq);
        Assert.True(result.correct);
        Assert.True(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct,
                true,
                0,
                session.ShouldIgnoreAudio(DateTime.UtcNow),
                heardHz: freq,
                accentedClickHz: WaitingCountInSettings.DefaultAccentedPitchHz,
                unaccentedClickHz: WaitingCountInSettings.DefaultUnaccentedPitchHz));
    }

    [Fact]
    public void MatchingPitch_AfterGuard_IsAcceptedNormally()
    {
        var session = CreateCMajorSession();
        session.StartListeningClock();
        Assert.False(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(freq);
        Assert.True(result.correct);
        Assert.True(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct, true, 0, session.ShouldIgnoreAudio(DateTime.UtcNow)));

        Assert.True(session.UpdateFeedbackForCurrent(freq, result));
        Assert.Contains(0, session.CorrectNoteIndices);
        Assert.Equal(1, session.CurrentNoteIndex);
    }

    [Fact]
    public void WrongPitch_AfterGuard_IsHandledNormally()
    {
        var session = CreateCMajorSession();
        session.StartListeningClock();

        double wrongFreq = NoteSessionService.MidiToFreqPublic(62);
        var result = session.Evaluate(wrongFreq);
        Assert.False(result.correct);
        Assert.False(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct, true, 0, session.ShouldIgnoreAudio(DateTime.UtcNow)));

        Assert.True(session.UpdateFeedbackForCurrent(wrongFreq, result));
        Assert.Equal(0, session.CurrentNoteIndex);
        Assert.True(session.NoteFeedbacks.TryGetValue(0, out var fb) && fb.Wrong > 0);
        Assert.DoesNotContain(0, session.CorrectNoteIndices);
    }

    [Fact]
    public void ExactSamePitchAsCountInTone_StillSuppressedDuringClick()
    {
        var session = CreateSessionWithFirstMidi(81); // A6 == default accented Count-In Hz
        session.StartListeningClock();
        session.SuppressCountInClickSelfSound(clickDurationMs: 150);

        double countInHz = WaitingCountInSettings.DefaultAccentedPitchHz;
        var result = session.Evaluate(countInHz);
        Assert.True(result.correct, "Count-In Hz should evaluate as the expected first note");
        Assert.False(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct, true, 0, session.ShouldIgnoreAudio(DateTime.UtcNow),
                heardHz: countInHz,
                accentedClickHz: WaitingCountInSettings.DefaultAccentedPitchHz,
                unaccentedClickHz: WaitingCountInSettings.DefaultUnaccentedPitchHz));
        Assert.Equal(0, session.CurrentNoteIndex);
        Assert.DoesNotContain(0, session.CorrectNoteIndices);
    }

    [Fact]
    public void SuppressCountInClickSelfSound_ExtendsIgnoreWindow()
    {
        var session = CreateCMajorSession();
        Assert.False(session.ShouldIgnoreAudio(DateTime.UtcNow));
        session.SuppressCountInClickSelfSound(50);
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));
        Assert.True(session.IgnoreAudioUntilUtc > DateTime.UtcNow.AddMilliseconds(40));
    }

    private static NoteSessionService CreateCMajorSession()
        => CreateSessionWithFirstMidi(60);

    private static NoteSessionService CreateSessionWithFirstMidi(int firstMidi)
    {
        int[] midis = [firstMidi, firstMidi + 2, firstMidi + 4, firstMidi + 5];
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = 60,
            ShowConductorCues = true,
            ChildLevel = 1,
            SampleRate = 44100,
            PitchWindowSize = 4096,
        };
        session.Reset();
        session.Tune = "Selected Scale";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.Tempo = 60;
        session.MeterTimeSignature = "4/4";
        session.SampleRate = 44100;
        session.PitchWindowSize = 4096;

        session.NotesToDraw.Clear();
        session.FeedbackViewModels.Clear();
        for (int i = 0; i < midis.Length; i++)
        {
            int midi = midis[i];
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
                DurationBeats = 1,
                Duration = NoteDuration.Quarter,
                StartBeat = i,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        return session;
    }
}

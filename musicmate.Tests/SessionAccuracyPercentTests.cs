using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class SessionAccuracyPercentTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public SessionAccuracyPercentTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void CompletionPercent_IgnoresRetryWrongCounters()
    {
        var session = CreateSession(noteCount: 15);
        for (int i = 0; i < 15; i++)
        {
            session.CorrectNoteIndices.Add(i);
            session.NoteFeedbacks[i] = (Wrong: i < 14 ? 1 : 0, Cents: 0); // 14 retry wrongs
        }

        var (_, wrongRetries, retryWeighted) = session.GetSessionCorrectWrongTotals();
        Assert.Equal(14, wrongRetries);
        Assert.InRange(retryWeighted, 51.0, 52.0); // legacy ~51.7%

        Assert.Equal(100.0, session.GetSessionCompletionPercent());
    }

    [Fact]
    public void LevelUpPitchAccuracy_UsesWrongPitchOutcomesOnly()
    {
        var session = CreateSession(noteCount: 4);
        for (int i = 0; i < 4; i++)
            session.CorrectNoteIndices.Add(i);

        // Timing-early reject with correct pitch, then accept — pitch outcomes stay pitch-correct.
        session.RecordAttemptOutcome(new NoteAttemptOutcome
        {
            NoteIndex = 0,
            ExpectedWrittenNoteName = "C4",
            ActualDetectedNoteName = "C4",
            PitchCorrect = true,
            TimingCorrect = false,
            OverallCorrect = false,
            WrongReason = "Early",
            PitchErrorCents = 0,
        });
        session.RecordAttemptOutcome(new NoteAttemptOutcome
        {
            NoteIndex = 0,
            ExpectedWrittenNoteName = "C4",
            ActualDetectedNoteName = "C4",
            PitchCorrect = true,
            TimingCorrect = true,
            OverallCorrect = true,
            WrongReason = string.Empty,
            PitchErrorCents = 0,
        });

        Assert.Equal(100.0, session.GetSessionLevelUpPitchAccuracyPercent());

        session.RecordAttemptOutcome(new NoteAttemptOutcome
        {
            NoteIndex = 1,
            ExpectedWrittenNoteName = "D4",
            ActualDetectedNoteName = "E4",
            PitchCorrect = false,
            TimingCorrect = true,
            OverallCorrect = false,
            WrongReason = "WrongPitch",
            PitchErrorCents = 100,
        });

        // Early row is superseded by the accept; wrong-pitch row remains.
        var (pr, pw, _, _, _, _, _, _) = session.GetSessionSummaryCounts();
        Assert.Equal(1, pr);
        Assert.Equal(1, pw);
        Assert.Equal(50.0, session.GetSessionLevelUpPitchAccuracyPercent());
    }

    [Fact]
    public void BuildSessionStat_StoresCompletionAsPc_AndRetryWeightedAsPcRaw()
    {
        var session = CreateSession(noteCount: 10);
        for (int i = 0; i < 10; i++)
        {
            session.CorrectNoteIndices.Add(i);
            session.NoteFeedbacks[i] = (Wrong: 1, Cents: 0);
        }

        var stat = PracticeSessionPersistence.BuildSessionStat(session);
        Assert.Equal(100.0, stat.Pc);
        Assert.Equal(100.0, stat.Pch);
        Assert.Equal(50.0, stat.PcRaw);
    }

    [Fact]
    public void BuildSessionResult_UsesLevelUpPitchAccuracyArgument()
    {
        var session = CreateSession(noteCount: 8);
        for (int i = 0; i < 8; i++)
            session.CorrectNoteIndices.Add(i);

        var result = PracticeSessionPersistence.BuildSessionResult(session, pitchAccuracyPercent: 87.5);
        Assert.Equal(87.5, result.PitchAccuracyPercent);
        Assert.Equal(8, result.TotalNotes);
        Assert.Equal(8, result.CorrectPitchCount);
    }

    [Fact]
    public void CaptureCompletionSummary_UsesNotesCompletedNotRetries()
    {
        var session = CreateSession(noteCount: 10);
        for (int i = 0; i < 8; i++)
        {
            session.CorrectNoteIndices.Add(i);
            session.NoteFeedbacks[i] = (Wrong: 3, Cents: 0);
        }

        var summary = PracticeSessionLifecycle.CaptureCompletionSummary(session);
        Assert.Equal(8, summary.Correct);
        Assert.Equal(2, summary.Wrong); // remaining incomplete notes
        Assert.Equal(80.0, summary.AccuracyPercent);
    }

    private static NoteSessionService CreateSession(int noteCount)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = 60,
            ChildLevel = 1,
        };
        session.Reset();
        session.Instrument = "concert-pitch";
        session.ChildLevel = 1;
        session.NotesToDraw.Clear();
        session.FeedbackViewModels.Clear();
        for (int i = 0; i < noteCount; i++)
        {
            int midi = 60 + (i % 8);
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
                DurationBeats = 1,
                Duration = NoteDuration.Quarter,
                StartBeat = i,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        return session;
    }
}

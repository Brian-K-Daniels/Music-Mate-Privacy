using System.Diagnostics;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class PlaybackOnsetScheduleTests
{
    public static IEnumerable<object[]> Tempos { get; } =
    [
        [30],
        [60],
        [120],
        [180],
        [230],
        [NoteSessionService.MaxTempo],
    ];

    [Theory]
    [MemberData(nameof(Tempos))]
    public void QuarterNoteOnsets_UseOneStartTime(int bpm)
    {
        double ms = 60000.0 / bpm;
        var slots = Quarters(8, bpm);

        Assert.Equal(0, slots[0].OnsetMs, precision: 6);
        for (int i = 0; i < slots.Count; i++)
            Assert.Equal(i * ms, slots[i].OnsetMs, precision: 6);

        if (bpm == 230)
        {
            Assert.Equal(0, slots[0].OnsetMs, precision: 2);
            Assert.Equal(260.87, slots[1].OnsetMs, precision: 2);
            Assert.Equal(521.74, slots[2].OnsetMs, precision: 2);
            Assert.Equal(782.61, slots[3].OnsetMs, precision: 2);
            Assert.Equal(1043.48, slots[4].OnsetMs, precision: 2);
        }
    }

    [Fact]
    public void LongQuarterSequence_DoesNotDriftAwayFromTheBeatGrid()
    {
        const int bpm = 230;
        const int count = 200;
        double ms = 60000.0 / bpm;
        var slots = Quarters(count, bpm);

        Assert.Equal((count - 1) * ms, slots[^1].OnsetMs, precision: 6);
        Assert.Equal(count * ms, slots[^1].OnsetMs + slots[^1].SpanMs, precision: 6);

        double chainedRounded = 0;
        double roundedStep = Math.Round(ms);
        for (int i = 0; i < count; i++)
            chainedRounded += roundedStep;
        Assert.True(Math.Abs(chainedRounded - count * ms) > 1.0);
        Assert.Equal(count * ms, slots[^1].OnsetMs + slots[^1].SpanMs, precision: 4);
    }

    [Fact]
    public void RestsAndShorterNotes_KeepTheirPlaceOnTheGrid()
    {
        const int bpm = 120;
        double ms = 60000.0 / bpm;
        var slots = PlaybackOnsetSchedule.Build(
            [1.0, 0.5, 1.0, 0.25],
            [true, false, false, false],
            [0, 440, 440, 440],
            bpm);

        Assert.True(slots[0].IsRest);
        Assert.Equal(0, slots[0].SoundMs, precision: 6);
        Assert.Equal(ms, slots[1].OnsetMs, precision: 6);
        Assert.Equal(ms + 0.5 * ms, slots[2].OnsetMs, precision: 6);
        Assert.Equal(ms + 1.5 * ms, slots[3].OnsetMs, precision: 6);
        Assert.Equal(0.25 * ms, slots[3].SpanMs, precision: 6);
        Assert.True(slots[1].SoundMs < slots[1].SpanMs);
        Assert.True(slots[1].SoundMs + slots[1].GapMs <= slots[1].SpanMs + 0.001);
    }

    [Fact]
    public void ChangingTempo_ScalesEveryOnset()
    {
        var slow = Quarters(6, 60);
        var fast = Quarters(6, 120);
        for (int i = 0; i < slow.Count; i++)
            Assert.Equal(slow[i].OnsetMs / 2.0, fast[i].OnsetMs, precision: 6);
    }

    [Fact]
    public void RepeatingPlayback_AndADifferentPitch_DoNotChangeOnsets()
    {
        var first = PlaybackOnsetSchedule.Build([1, 1, 1, 1], [false, false, false, false], [440, 440, 440, 440], 180);
        var again = PlaybackOnsetSchedule.Build([1, 1, 1, 1], [false, false, false, false], [440, 440, 440, 440], 180);
        var otherPitch = PlaybackOnsetSchedule.Build([1, 1, 1, 1], [false, false, false, false], [880, 660, 550, 494], 180);

        Assert.Equal(first.Select(s => s.OnsetMs), again.Select(s => s.OnsetMs));
        Assert.Equal(first.Select(s => s.OnsetMs), otherPitch.Select(s => s.OnsetMs));
        Assert.NotEqual(first[1].FrequencyHz, otherPitch[1].FrequencyHz);
    }

    [Fact]
    public void Pause_MovesOnlyTheNotesThatHaveNotStarted()
    {
        double third = PlaybackOnsetSchedule.OnsetMs(2, 230);
        double shift = PlaybackOnsetSchedule.PauseShiftMs(pauseAtMs: 100, resumeAtMs: 350);
        Assert.Equal(250, shift, precision: 6);
        Assert.Equal(0, PlaybackOnsetSchedule.OnsetAfterPause(0, 100, shift), precision: 6);
        Assert.Equal(third + shift, PlaybackOnsetSchedule.OnsetAfterPause(third, 100, shift), precision: 6);
    }

    [Theory]
    [MemberData(nameof(Tempos))]
    public void CountIn_LandsOnTheSameGridAsTheFirstMusicNote(int bpm)
    {
        double ms = WaitingCountInLogic.MsPerBeat(bpm);
        double afterFour = PlaybackOnsetSchedule.FirstNoteOnsetAfterCountIn(4, bpm);
        Assert.Equal(WaitingCountInLogic.GetAbsoluteBeatStartMs(4, ms), afterFour, precision: 6);
        Assert.Equal(afterFour + ms, afterFour + PlaybackOnsetSchedule.OnsetMs(1, bpm), precision: 6);
    }

    [Fact]
    public void RenderedBuffer_PlacesAttacksOnTheSampleClock_AndLeavesTheGapSilent()
    {
        const int bpm = 230;
        var slots = Quarters(4, bpm, 440);
        short[] pcm = PlaybackOnsetSchedule.RenderPcm(slots, 0.22f, AudioTonePcm.SampleRate);

        int attack = SampleAt(slots[1].OnsetMs + 20);
        int gap = SampleAt(slots[1].OnsetMs - 4);
        int secondAttack = SampleAt(slots[2].OnsetMs + 20);
        Assert.InRange(attack, 0, pcm.Length - 1);
        Assert.NotEqual(0, pcm[attack]);
        Assert.Equal(0, pcm[gap]);
        Assert.NotEqual(0, pcm[secondAttack]);
        Assert.True(pcm.Length >= SampleAt(slots[^1].OnsetMs + slots[^1].SpanMs) - 1);
    }

    [Fact]
    public async Task Scheduler_At230_StaysOnTheGrid_WhenHighlightWorkIsSlow()
    {
        var slots = Quarters(8, 230);
        var hits = new List<double>();
        var clock = Stopwatch.StartNew();
        await PlaybackOnsetSchedule.RunAsync(
            slots,
            slot => hits.Add(clock.Elapsed.TotalMilliseconds),
            highlight: _ => Thread.Sleep(300),
            CancellationToken.None);

        Assert.Equal(slots.Count, hits.Count);
        for (int i = 0; i < hits.Count; i++)
            Assert.InRange(hits[i] - slots[i].OnsetMs, -5, 25);

        double drift = (hits[^1] - slots[^1].OnsetMs) - (hits[0] - slots[0].OnsetMs);
        Assert.InRange(drift, -20, 20);
    }

    [Fact]
    public async Task Scheduler_AtMaximumTempo_DoesNotAccumulateDelay()
    {
        int bpm = NoteSessionService.MaxTempo;
        var slots = Quarters(8, bpm);
        var hits = new List<double>();
        var clock = Stopwatch.StartNew();
        await PlaybackOnsetSchedule.RunAsync(
            slots,
            slot => hits.Add(clock.Elapsed.TotalMilliseconds),
            highlight: null,
            CancellationToken.None);

        double drift = (hits[^1] - slots[^1].OnsetMs) - (hits[0] - slots[0].OnsetMs);
        Assert.InRange(drift, -20, 20);
    }

    [Fact]
    public async Task PauseAndResume_HoldTheFollowingOnsets()
    {
        var slots = Quarters(4, 230);
        var hold = new PlaybackHold();
        var hits = new List<double>();
        var clock = Stopwatch.StartNew();
        int paused = 0;
        int resumed = 0;
        await PlaybackOnsetSchedule.RunAsync(
            slots,
            slot =>
            {
                hits.Add(clock.Elapsed.TotalMilliseconds);
                if (slot.Index == 0)
                {
                    hold.Pause();
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(180);
                        hold.Resume();
                    });
                }
            },
            highlight: null,
            CancellationToken.None,
            hold,
            pauseAudio: () => Interlocked.Increment(ref paused),
            resumeAudio: () => Interlocked.Increment(ref resumed));

        double gap = hits[1] - hits[0];
        double beat = slots[1].OnsetMs - slots[0].OnsetMs;
        Assert.InRange(gap - beat, 140, 260);
        Assert.True(paused >= 1);
        Assert.True(resumed >= 1);
    }

    [Fact]
    public async Task Stop_CancelsBeforeLaterNotes()
    {
        var slots = Quarters(6, 230);
        var hits = new List<double>();
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PlaybackOnsetSchedule.RunAsync(
                slots,
                slot =>
                {
                    hits.Add(1);
                    if (slot.Index == 0)
                        cts.Cancel();
                },
                highlight: _ => Thread.Sleep(50),
                cts.Token));

        Assert.Single(hits);
    }

    private static IReadOnlyList<PlaybackSlot> Quarters(int count, int bpm, double frequency = 440)
    {
        var durations = new double[count];
        var rests = new bool[count];
        var frequencies = new double[count];
        for (int i = 0; i < count; i++)
        {
            durations[i] = 1;
            frequencies[i] = frequency;
        }

        return PlaybackOnsetSchedule.Build(durations, rests, frequencies, bpm);
    }

    private static int SampleAt(double ms)
        => (int)Math.Round(ms / 1000.0 * AudioTonePcm.SampleRate);
}

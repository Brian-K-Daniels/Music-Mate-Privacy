using System.Diagnostics;
using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Absolute onset times for Play. Each onset is beat-position × one beat at the
/// session tempo, measured from a single start. Durations are not rounded and
/// then added again for the next note.
/// </summary>
public readonly record struct PlaybackSlot(
    int Index,
    double OnsetMs,
    double SpanMs,
    double SoundMs,
    double GapMs,
    bool IsRest,
    double FrequencyHz);

public static class PlaybackOnsetSchedule
{
    /// <summary>Same articulation gap Play used before the shared clock: 5% of the slot, at most 20 ms.</summary>
    public const double GapFraction = 0.05;
    public const double MaxGapMs = 20.0;

    public static double OnsetMs(double cumulativeBeats, int bpm)
        => cumulativeBeats * ConductorOnsetTiming.MsPerBeat(bpm);

    /// <summary>Where the first music note starts after a count-in of <paramref name="countInBeats"/> on the same grid.</summary>
    public static double FirstNoteOnsetAfterCountIn(int countInBeats, int bpm)
        => OnsetMs(Math.Max(0, countInBeats), bpm);

    public static double PauseShiftMs(double pauseAtMs, double resumeAtMs)
        => Math.Max(0.0, resumeAtMs - pauseAtMs);

    /// <summary>Onsets that have not yet started move forward by the time Play was held.</summary>
    public static double OnsetAfterPause(double onsetMs, double pauseAtMs, double shiftMs)
        => onsetMs >= pauseAtMs - 0.0001 ? onsetMs + Math.Max(0.0, shiftMs) : onsetMs;

    public static IReadOnlyList<PlaybackSlot> FromGenerated(IReadOnlyList<GeneratedNote> notes, int bpm)
    {
        var durations = new double[notes.Count];
        var rests = new bool[notes.Count];
        var frequencies = new double[notes.Count];
        for (int i = 0; i < notes.Count; i++)
        {
            durations[i] = notes[i].BeatDuration;
            frequencies[i] = notes[i].TargetFrequency;
            rests[i] = notes[i].IsRest || notes[i].TargetFrequency <= 0 || double.IsNaN(notes[i].TargetFrequency);
        }

        return Build(durations, rests, frequencies, bpm);
    }

    public static IReadOnlyList<PlaybackSlot> FromSessionNotes(IReadOnlyList<NoteInfo> notes, int bpm)
    {
        var durations = new double[notes.Count];
        var rests = new bool[notes.Count];
        var frequencies = new double[notes.Count];
        for (int i = 0; i < notes.Count; i++)
        {
            durations[i] = notes[i].Duration?.ToBeatValue() ?? 1.0;
            frequencies[i] = notes[i].TargetFreq;
            rests[i] = notes[i].IsRest || notes[i].TargetFreq <= 0 || double.IsNaN(notes[i].TargetFreq);
        }

        return Build(durations, rests, frequencies, bpm);
    }

    public static IReadOnlyList<PlaybackSlot> Build(
        IReadOnlyList<double> beatDurations,
        IReadOnlyList<bool> rests,
        IReadOnlyList<double> frequencies,
        int bpm)
    {
        bpm = Math.Clamp(bpm, NoteSessionService.MinTempo, NoteSessionService.MaxTempo);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double beats = 0;
        var slots = new PlaybackSlot[beatDurations.Count];
        for (int i = 0; i < beatDurations.Count; i++)
        {
            double durationBeats = Math.Max(0, beatDurations[i]);
            double onset = beats * msPerBeat;
            double span = durationBeats * msPerBeat;
            double gap = Math.Min(MaxGapMs, span * GapFraction);
            bool rest = i < rests.Count && rests[i];
            double frequency = i < frequencies.Count ? frequencies[i] : 0;
            if (frequency <= 0 || double.IsNaN(frequency) || double.IsInfinity(frequency))
                rest = true;
            double sound = rest ? 0 : Math.Max(0, span - gap);
            slots[i] = new PlaybackSlot(i, onset, span, sound, rest ? span : gap, rest, rest ? 0 : frequency);
            beats += durationBeats;
        }

        return slots;
    }

    /// <summary>
    /// One PCM buffer for the whole sequence. Sample positions come from the
    /// absolute onset, so a late note cannot push the notes after it.
    /// </summary>
    public static short[] RenderPcm(IReadOnlyList<PlaybackSlot> slots, float volume, int sampleRate)
    {
        if (sampleRate <= 0)
            sampleRate = AudioTonePcm.SampleRate;

        double endMs = 0;
        foreach (var slot in slots)
            endMs = Math.Max(endMs, slot.OnsetMs + slot.SpanMs);

        int total = Math.Max(1, (int)Math.Ceiling(endMs / 1000.0 * sampleRate));
        var pcm = new short[total];
        float gain = Math.Clamp(volume, 0f, 1f);
        foreach (var slot in slots)
        {
            if (slot.IsRest || slot.SoundMs <= 0 || slot.FrequencyHz <= 0)
                continue;

            short[] tone = AudioTonePcm.Build(
                slot.FrequencyHz,
                slot.SoundMs / 1000.0,
                gain,
                seamlessLoop: false,
                out int audibleSamples,
                out _);
            int start = (int)Math.Round(slot.OnsetMs / 1000.0 * sampleRate);
            int copy = Math.Min(audibleSamples, tone.Length);
            for (int i = 0; i < copy && start + i < pcm.Length; i++)
                pcm[start + i] = tone[i];
        }

        return pcm;
    }

    /// <summary>
    /// Wait until each onset on one stopwatch. Audio is started by
    /// <paramref name="startAudio"/> and is not delayed by <paramref name="highlight"/>.
    /// </summary>
    public static async Task RunAsync(
        IReadOnlyList<PlaybackSlot> slots,
        Action<PlaybackSlot> startAudio,
        Action<PlaybackSlot>? highlight,
        CancellationToken ct,
        PlaybackHold? hold = null,
        Action? onClockStarted = null,
        Action? pauseAudio = null,
        Action? resumeAudio = null)
    {
        var clock = Stopwatch.StartNew();
        onClockStarted?.Invoke();
        double shiftMs = 0;
        foreach (var slot in slots)
        {
            ct.ThrowIfCancellationRequested();
            await WaitForOnsetAsync(
                    clock, slot.OnsetMs, hold, shiftMs, s => shiftMs = s, pauseAudio, resumeAudio, ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            startAudio(slot);
            QueueHighlight(highlight, slot);
        }

        if (slots.Count > 0)
        {
            var last = slots[^1];
            await WaitForOnsetAsync(
                    clock, last.OnsetMs + last.SpanMs, hold, shiftMs, s => shiftMs = s, pauseAudio, resumeAudio, ct)
                .ConfigureAwait(false);
        }
    }

    private static void QueueHighlight(Action<PlaybackSlot>? highlight, PlaybackSlot slot)
    {
        if (highlight == null)
            return;

        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var (callback, item) = ((Action<PlaybackSlot>, PlaybackSlot))state!;
                try { callback(item); } catch { }
            },
            (highlight, slot));
    }

    private static async Task WaitForOnsetAsync(
        Stopwatch clock,
        double onsetMs,
        PlaybackHold? hold,
        double shiftMs,
        Action<double> setShift,
        Action? pauseAudio,
        Action? resumeAudio,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (hold != null && hold.IsPaused)
            {
                try { pauseAudio?.Invoke(); } catch { }
                double added = await hold.WaitForResumeAsync(ct).ConfigureAwait(false);
                shiftMs += added;
                setShift(shiftMs);
                try { resumeAudio?.Invoke(); } catch { }
            }

            double wallTarget = onsetMs + shiftMs;
            double remaining = wallTarget - clock.Elapsed.TotalMilliseconds;
            if (remaining <= 0.5)
                return;

            // Wake often enough to notice Pause without spinning for the whole beat.
            double slice = remaining > 40.0
                ? clock.Elapsed.TotalMilliseconds + 30.0
                : wallTarget;
            await WaitingCountInPlayer.WaitUntilAsync(clock, slice, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Freezes playback time. The held duration is added to every later onset.</summary>
public sealed class PlaybackHold
{
    private readonly object _gate = new();
    private TaskCompletionSource<double>? _resume;
    private long _pausedAtTimestamp;
    private int _paused;

    public bool IsPaused => Volatile.Read(ref _paused) != 0;

    public void Pause()
    {
        lock (_gate)
        {
            if (_resume != null)
                return;
            _pausedAtTimestamp = Stopwatch.GetTimestamp();
            _resume = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _paused, 1);
        }
    }

    public void Resume()
    {
        TaskCompletionSource<double>? resume;
        double shift;
        lock (_gate)
        {
            resume = _resume;
            if (resume == null)
                return;
            _resume = null;
            Volatile.Write(ref _paused, 0);
            long elapsed = Stopwatch.GetTimestamp() - _pausedAtTimestamp;
            shift = Math.Max(0, elapsed * 1000.0 / Stopwatch.Frequency);
        }

        resume.TrySetResult(shift);
    }

    public async Task<double> WaitForResumeAsync(CancellationToken ct)
    {
        Task<double>? wait;
        lock (_gate)
            wait = _resume?.Task;
        if (wait == null)
            return 0;

        await using var _ = ct.Register(() =>
        {
            lock (_gate)
                _resume?.TrySetCanceled(ct);
        }).ConfigureAwait(false);
        return await wait.ConfigureAwait(false);
    }
}

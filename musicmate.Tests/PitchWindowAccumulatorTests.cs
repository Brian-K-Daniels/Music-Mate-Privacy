using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class PitchWindowAccumulatorTests
{
    private const int SampleRate = 44100;
    private const int BlockSize = 1024;
    private const int WindowSize = 4096;
    private const float Onset = NoteSessionService.DefaultRmsThreshold; // 0.025

    private static readonly float Hold = PitchWindowAccumulator.HoldThreshold(Onset);

    [Fact]
    public void HoldThreshold_IsHalfOfOnset()
    {
        Assert.Equal(0.0125f, Hold, precision: 4);
        Assert.Equal(2, PitchWindowAccumulator.MaxQuietHoldBlocks);
    }

    [Fact]
    public void SteadyF3AboveOnset_FillsWindowAndDetects()
    {
        var acc = NewAccumulator();
        var blocks = Blocks(174.614, count: 4, amplitude: 0.08f);
        Assert.Equal(PitchWindowIngestKind.WindowReady, IngestAll(acc, blocks, out bool onset));
        Assert.True(onset);
        AssertDetected(acc, expectedHz: 174.614, label: "F3");
    }

    [Fact]
    public void SteadyG3AboveOnset_FillsWindowAndDetects()
    {
        var acc = NewAccumulator();
        Assert.Equal(PitchWindowIngestKind.WindowReady,
            IngestAll(acc, Blocks(195.998, 4, 0.08f), out _));
        AssertDetected(acc, 195.998, "G3");
    }

    [Fact]
    public void C4AndG4Controls_StillDetect()
    {
        var c4 = NewAccumulator();
        Assert.Equal(PitchWindowIngestKind.WindowReady,
            IngestAll(c4, Blocks(261.626, 4, 0.08f), out _));
        AssertDetected(c4, 261.626, "C4");

        var g4 = NewAccumulator();
        Assert.Equal(PitchWindowIngestKind.WindowReady,
            IngestAll(g4, Blocks(391.995, 4, 0.08f), out _));
        AssertDetected(g4, 391.995, "G4");
    }

    [Fact]
    public void F3DipBelowOnsetButAboveHold_StillFills_OldResetWouldFail()
    {
        // One 1024-sample block at ~0.02 RMS (below 0.025, above 0.0125).
        var blocks = Blocks(174.614, 4, 0.08f);
        blocks[1] = ScaleBlock(blocks[1], 0.028f / 0.08f);
        Assert.InRange(PitchDetectionService.ComputeRms(blocks[1]), 0.0126f, 0.0249f);

        var oldPos = SimulateOldReset(blocks);
        Assert.True(oldPos < WindowSize, "Old logic must fail to fill after a single dip.");

        var acc = NewAccumulator();
        var kind = IngestSequence(acc, blocks, out _);
        Assert.Equal(PitchWindowIngestKind.WindowReady, kind);
        AssertDetected(acc, 174.614, "F3");
    }

    [Fact]
    public void G3DipBelowOnset_StillDetects()
    {
        var blocks = Blocks(195.998, 4, 0.08f);
        blocks[1] = ScaleBlock(blocks[1], 0.028f / 0.08f);
        Assert.InRange(PitchDetectionService.ComputeRms(blocks[1]), 0.0126f, 0.0249f);

        var acc = NewAccumulator();
        Assert.Equal(PitchWindowIngestKind.WindowReady,
            IngestSequence(acc, blocks, out _));
        AssertDetected(acc, 195.998, "G3");
    }

    [Fact]
    public void PianoLikeDecay_F3_ReachesDetection()
    {
        // Onset, then decaying amplitudes crossing the 0.025 gate.
        var blocks = Blocks(174.614, 4, 0.08f);
        blocks[1] = ScaleBlock(blocks[1], 0.05f / 0.08f);
        blocks[2] = ScaleBlock(blocks[2], 0.028f / 0.08f);
        blocks[3] = ScaleBlock(blocks[3], 0.012f / 0.08f);
        Assert.True(PitchDetectionService.ComputeRms(blocks[2]) < Onset);
        Assert.True(PitchDetectionService.ComputeRms(blocks[2]) >= Hold);
        Assert.True(PitchDetectionService.ComputeRms(blocks[3]) < Hold);

        var oldPos = SimulateOldReset(blocks);
        Assert.True(oldPos < WindowSize);

        var acc = NewAccumulator();
        Assert.Equal(PitchWindowIngestKind.WindowReady, IngestSequence(acc, blocks, out _));
        AssertDetected(acc, 174.614, "F3 decay");
    }

    [Fact]
    public void BelowHoldBlocks_AreToleratedUpToTwo_ThenReset()
    {
        float[] loud = SineBlock(174.614, 0.08f);
        float[] quiet = SineBlock(174.614, 0.008f);
        Assert.True(PitchDetectionService.ComputeRms(quiet) < Hold);

        var acc = NewAccumulator();
        Assert.Equal(PitchWindowIngestKind.Accumulating, acc.Ingest(loud, Rms(loud), Onset, false).Kind);
        Assert.Equal(PitchWindowIngestKind.Accumulating, acc.Ingest(quiet, Rms(quiet), Onset, false).Kind);
        Assert.Equal(1, acc.ConsecutiveQuietBlocks);
        Assert.Equal(PitchWindowIngestKind.Accumulating, acc.Ingest(quiet, Rms(quiet), Onset, false).Kind);
        Assert.Equal(2, acc.ConsecutiveQuietBlocks);
        Assert.True(acc.HasSamples);

        var third = acc.Ingest(quiet, Rms(quiet), Onset, false);
        Assert.Equal(PitchWindowIngestKind.BecameSilent, third.Kind);
        Assert.False(acc.HasSamples);
        Assert.Equal(0, acc.Filled);
    }

    [Fact]
    public void SilenceAndNoiseBelowOnset_NeverStart()
    {
        var acc = NewAccumulator();
        float[] noise = new float[BlockSize];
        var rng = new Random(1);
        for (int i = 0; i < noise.Length; i++)
            noise[i] = (float)((rng.NextDouble() - 0.5) * 0.01);

        Assert.True(Rms(noise) < Onset);
        for (int i = 0; i < 8; i++)
        {
            var outcome = acc.Ingest(noise, Rms(noise), Onset, false);
            Assert.Equal(PitchWindowIngestKind.IgnoredQuiet, outcome.Kind);
            Assert.False(outcome.StartedNewOnset);
        }

        Assert.False(acc.HasSamples);
    }

    [Fact]
    public void TwoSuccessiveNotes_ShortGap_DoesNotLeakPreviousPitch()
    {
        var acc = NewAccumulator();
        Assert.Equal(PitchWindowIngestKind.WindowReady,
            IngestAll(acc, Blocks(174.614, 4, 0.08f), out _));
        acc.HopHalf();

        float[] silence = new float[BlockSize];
        // Two below-hold blocks are held (may fill the hopped window); the third resets.
        var q1 = acc.Ingest(silence, 0f, Onset, false);
        Assert.Equal(PitchWindowIngestKind.Accumulating, q1.Kind);
        var q2 = acc.Ingest(silence, 0f, Onset, false);
        Assert.True(q2.Kind is PitchWindowIngestKind.Accumulating or PitchWindowIngestKind.WindowReady);
        if (q2.Kind == PitchWindowIngestKind.WindowReady)
            acc.HopHalf();
        var q3 = acc.Ingest(silence, 0f, Onset, false);
        Assert.Equal(PitchWindowIngestKind.BecameSilent, q3.Kind);
        Assert.False(acc.HasSamples);

        Assert.Equal(PitchWindowIngestKind.WindowReady,
            IngestAll(acc, Blocks(195.998, 4, 0.08f), out bool newOnset));
        Assert.True(newOnset);
        AssertDetected(acc, 195.998, "G3 after F3");
    }

    [Fact]
    public void LoudAfterQuietHold_StartsFreshWindow_NoStaleMix()
    {
        var acc = NewAccumulator();
        float[] f3 = SineBlock(174.614, 0.08f);
        float[] quiet = new float[BlockSize];
        var g3Blocks = Blocks(195.998, 4, 0.08f);

        acc.Ingest(f3, Rms(f3), Onset, false);
        acc.Ingest(quiet, 0f, Onset, false); // quiet hold, F3 still in buffer
        Assert.True(acc.HasSamples);

        var onset = acc.Ingest(g3Blocks[0], Rms(g3Blocks[0]), Onset, false);
        Assert.True(onset.StartedNewOnset);
        Assert.Equal(BlockSize, acc.Filled);

        IngestSequence(acc, g3Blocks[1..], out _);
        Assert.Equal(WindowSize, acc.Filled);
        AssertDetected(acc, 195.998, "G3 fresh after F3 tail");
    }

    [Fact]
    public void IgnoreAudio_ClearsBufferWithoutStartingOnset()
    {
        var acc = NewAccumulator();
        float[] loud = SineBlock(261.626, 0.08f);
        acc.Ingest(loud, Rms(loud), Onset, false);
        Assert.True(acc.HasSamples);

        var outcome = acc.Ingest(loud, Rms(loud), Onset, ignoreAudio: true);
        Assert.Equal(PitchWindowIngestKind.DiscardedIgnorePeriod, outcome.Kind);
        Assert.False(outcome.StartedNewOnset);
        Assert.False(acc.HasSamples);
    }

    private static PitchWindowAccumulator NewAccumulator()
    {
        var acc = new PitchWindowAccumulator();
        acc.EnsureWindowSize(WindowSize);
        return acc;
    }

    private static PitchWindowIngestKind IngestAll(
        PitchWindowAccumulator acc, float[][] blocks, out bool sawOnset)
    {
        PitchWindowIngestKind last = PitchWindowIngestKind.IgnoredQuiet;
        sawOnset = false;
        foreach (var block in blocks)
        {
            var outcome = acc.Ingest(block, Rms(block), Onset, false);
            sawOnset |= outcome.StartedNewOnset;
            last = outcome.Kind;
        }
        return last;
    }

    private static PitchWindowIngestKind IngestSequence(
        PitchWindowAccumulator acc, float[][] blocks, out bool sawOnset)
        => IngestAll(acc, blocks, out sawOnset);

    private static int SimulateOldReset(float[][] blocks)
    {
        int pos = 0;
        foreach (var block in blocks)
        {
            if (Rms(block) < Onset)
            {
                pos = 0;
                continue;
            }

            pos += Math.Min(block.Length, WindowSize - pos);
            if (pos >= WindowSize)
                return pos;
        }
        return pos;
    }

    private static readonly object McLeodLock = new();

    private static void AssertDetected(PitchWindowAccumulator acc, double expectedHz, string label)
    {
        float freq;
        lock (McLeodLock)
        {
            freq = PitchDetectionService.DetectPitchMcLeod(acc.Buffer, acc.WindowSize, SampleRate);
        }
        Assert.True(freq > 0, $"{label}: McLeod returned 0");
        double cents = 1200 * Math.Log(freq / expectedHz, 2);
        Assert.InRange(cents, -50, 50);
    }

    private static float[][] Blocks(double hz, int count, float amplitude)
    {
        double phase = 0;
        var list = new float[count][];
        for (int i = 0; i < count; i++)
            list[i] = SineBlock(hz, amplitude, ref phase);
        return list;
    }

    private static float[] SineBlock(double hz, float amplitude)
    {
        double phase = 0;
        return SineBlock(hz, amplitude, ref phase);
    }

    private static float[] SineBlock(double hz, float amplitude, ref double phase)
    {
        var buf = new float[BlockSize];
        double step = 2 * Math.PI * hz / SampleRate;
        for (int i = 0; i < BlockSize; i++)
        {
            buf[i] = amplitude * (float)Math.Sin(phase);
            phase += step;
        }
        return buf;
    }

    private static float Rms(float[] block) => PitchDetectionService.ComputeRms(block);

    private static float[] ScaleBlock(float[] block, float gain)
    {
        var copy = new float[block.Length];
        for (int i = 0; i < block.Length; i++)
            copy[i] = block[i] * gain;
        return copy;
    }
}

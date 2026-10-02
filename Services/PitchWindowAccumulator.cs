namespace musicmate.Services
{
    /// <summary>
    /// Accumulates capture blocks into the McLeod analysis window with onset/hold RMS
    /// hysteresis so a brief quiet dip does not wipe a partially filled buffer.
    /// Music and Tuner share this path via <c>MusicPage.OnAudioBlock</c>.
    /// </summary>
    public sealed class PitchWindowAccumulator
    {
        /// <summary>
        /// Consecutive capture blocks below the hold threshold before the window is
        /// discarded as silence. Two 1024-sample blocks ≈ 46 ms at 44.1 kHz.
        /// </summary>
        public const int MaxQuietHoldBlocks = 2;

        /// <summary>Hold RMS = onset RMS × this ratio (0.025 → 0.0125).</summary>
        public const float HoldThresholdRatio = 0.5f;

        public static float HoldThreshold(float onsetThreshold)
            => Math.Max(0f, onsetThreshold * HoldThresholdRatio);

        private float[] _buffer = Array.Empty<float>();
        private int _filled;
        private int _consecutiveQuietBlocks;

        public int WindowSize => _buffer.Length;
        public int Filled => _filled;
        public int ConsecutiveQuietBlocks => _consecutiveQuietBlocks;
        public float[] Buffer => _buffer;
        public bool HasSamples => _filled > 0;

        public void EnsureWindowSize(int windowSize)
        {
            int size = Math.Max(1, windowSize);
            if (_buffer.Length == size)
                return;
            _buffer = new float[size];
            Reset();
        }

        public void Reset()
        {
            _filled = 0;
            _consecutiveQuietBlocks = 0;
        }

        /// <summary>
        /// Shift the window by half after a full-window analysis, matching the existing
        /// hop-overlap (second half kept; <see cref="Filled"/> becomes half).
        /// </summary>
        public void HopHalf()
        {
            if (_buffer.Length == 0)
                return;
            int hop = _buffer.Length / 2;
            int keep = _buffer.Length - hop;
            if (keep <= 0)
            {
                Reset();
                return;
            }

            Array.Copy(_buffer, hop, _buffer, 0, keep);
            _filled = keep;
        }

        public PitchWindowIngestOutcome Ingest(
            ReadOnlySpan<float> block,
            float rms,
            float onsetThreshold,
            bool ignoreAudio)
        {
            if (ignoreAudio)
            {
                Reset();
                return new PitchWindowIngestOutcome(PitchWindowIngestKind.DiscardedIgnorePeriod, startedNewOnset: false);
            }

            if (_buffer.Length == 0)
                EnsureWindowSize(4096);

            float onset = Math.Max(0f, onsetThreshold);
            float hold = HoldThreshold(onset);

            if (_filled == 0)
            {
                if (rms < onset)
                    return new PitchWindowIngestOutcome(PitchWindowIngestKind.IgnoredQuiet, startedNewOnset: false);

                Append(block);
                _consecutiveQuietBlocks = 0;
                return ReadyOrAccumulating(startedNewOnset: true);
            }

            if (rms >= onset)
            {
                // Loud again after below-hold blocks: treat as a new note/attack so
                // the previous tail is not mixed into the next analysis window.
                if (_consecutiveQuietBlocks > 0)
                {
                    Reset();
                    Append(block);
                    _consecutiveQuietBlocks = 0;
                    return ReadyOrAccumulating(startedNewOnset: true);
                }

                Append(block);
                _consecutiveQuietBlocks = 0;
                return ReadyOrAccumulating(startedNewOnset: false);
            }

            if (rms >= hold)
            {
                // Soft continuation of the same note (typical piano decay).
                Append(block);
                _consecutiveQuietBlocks = 0;
                return ReadyOrAccumulating(startedNewOnset: false);
            }

            _consecutiveQuietBlocks++;
            if (_consecutiveQuietBlocks <= MaxQuietHoldBlocks)
            {
                Append(block);
                return ReadyOrAccumulating(startedNewOnset: false);
            }

            Reset();
            return new PitchWindowIngestOutcome(PitchWindowIngestKind.BecameSilent, startedNewOnset: false);
        }

        private PitchWindowIngestOutcome ReadyOrAccumulating(bool startedNewOnset)
        {
            var kind = _filled >= _buffer.Length
                ? PitchWindowIngestKind.WindowReady
                : PitchWindowIngestKind.Accumulating;
            return new PitchWindowIngestOutcome(kind, startedNewOnset);
        }

        private void Append(ReadOnlySpan<float> block)
        {
            if (block.Length == 0 || _filled >= _buffer.Length)
                return;
            int toCopy = Math.Min(block.Length, _buffer.Length - _filled);
            block[..toCopy].CopyTo(_buffer.AsSpan(_filled, toCopy));
            _filled += toCopy;
        }
    }

    public enum PitchWindowIngestKind
    {
        /// <summary>Idle and below onset — block discarded, buffer stays empty.</summary>
        IgnoredQuiet,
        /// <summary>Samples kept; window not yet full.</summary>
        Accumulating,
        /// <summary>Window is full and ready for McLeod.</summary>
        WindowReady,
        /// <summary>Sustained below-hold silence; buffer cleared.</summary>
        BecameSilent,
        /// <summary>Ignore-audio window; buffer cleared without a silence event.</summary>
        DiscardedIgnorePeriod,
    }

    public readonly struct PitchWindowIngestOutcome
    {
        public PitchWindowIngestOutcome(PitchWindowIngestKind kind, bool startedNewOnset)
        {
            Kind = kind;
            StartedNewOnset = startedNewOnset;
        }

        public PitchWindowIngestKind Kind { get; }
        public bool StartedNewOnset { get; }
    }
}

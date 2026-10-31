namespace musicmate.Services
{
    /// <summary>
    /// PCM for generated sine tones. Fade-out plus trailing silence so the player
    /// can be released in digital zero instead of slamming a non-zero sample to DC
    /// (the usual “click at the end of every note” on phones).
    /// </summary>
    internal static class AudioTonePcm
    {
        public const int SampleRate = 44100;

        public static short[] Build(
            double freq,
            double durationSeconds,
            float volume,
            bool seamlessLoop,
            out int audibleSamples,
            out int padSamples)
        {
            if (seamlessLoop && freq > 0)
            {
                int cycles = Math.Max(1, (int)Math.Round(durationSeconds * freq));
                audibleSamples = Math.Max(1, (int)Math.Round(cycles * (double)SampleRate / freq));
                padSamples = 0;
            }
            else
            {
                audibleSamples = Math.Max(1, (int)(SampleRate * durationSeconds));
                padSamples = 0;
            }

            int fadeInSamples = 0;
            int fadeOutSamples = 0;
            if (!seamlessLoop)
            {
                fadeInSamples = Math.Min((int)(SampleRate * 0.008), Math.Max(1, audibleSamples / 8));
                double fadeOutSec = durationSeconds >= 0.08 ? 0.070 : 0.008;
                fadeOutSamples = Math.Min((int)(SampleRate * fadeOutSec), Math.Max(1, audibleSamples / 4));
                padSamples = durationSeconds >= 0.08
                    ? (int)(SampleRate * 0.080)
                    : (int)(SampleRate * 0.015);
            }

            var pcm = new short[audibleSamples + padSamples];
            double amp = short.MaxValue * Math.Clamp(volume, 0f, 1f);

            for (int i = 0; i < audibleSamples; i++)
            {
                double t = (double)i / SampleRate;
                double envelope = 1.0;
                if (fadeInSamples > 0 && i < fadeInSamples)
                    envelope = CosineRamp(i, fadeInSamples);
                else if (fadeOutSamples > 0 && i >= audibleSamples - fadeOutSamples)
                    envelope = CosineRamp(audibleSamples - 1 - i, fadeOutSamples);

                pcm[i] = (short)(Math.Sin(2 * Math.PI * freq * t) * amp * envelope);
            }

            return pcm;
        }

        /// <summary>0 at i=0, 1 at i=length (raised-cosine / Hann quarter).</summary>
        public static double CosineRamp(int i, int length)
        {
            if (length <= 1)
                return i <= 0 ? 0.0 : 1.0;
            return 0.5 - 0.5 * Math.Cos(Math.PI * i / length);
        }
    }
}

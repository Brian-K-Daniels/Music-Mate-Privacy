namespace musicmate.Services;

/// <summary>Short metronome PCM with minimal tail so high-tempo clicks do not overlap.</summary>
internal static class MetronomeClickPcm
{
    public const int SampleRate = AudioTonePcm.SampleRate;

    public static short[] Build(double freq, double durationSeconds, float volume)
    {
        int audibleSamples = Math.Max(1, (int)(SampleRate * durationSeconds));
        int fadeInSamples = Math.Min((int)(SampleRate * 0.004), Math.Max(1, audibleSamples / 10));
        int fadeOutSamples = Math.Min((int)(SampleRate * 0.006), Math.Max(1, audibleSamples / 6));
        int padSamples = (int)(SampleRate * 0.004);

        var pcm = new short[audibleSamples + padSamples];
        double amp = short.MaxValue * Math.Clamp(volume, 0f, 1f);

        for (int i = 0; i < audibleSamples; i++)
        {
            double t = (double)i / SampleRate;
            double envelope = 1.0;
            if (fadeInSamples > 0 && i < fadeInSamples)
                envelope = AudioTonePcm.CosineRamp(i, fadeInSamples);
            else if (fadeOutSamples > 0 && i >= audibleSamples - fadeOutSamples)
                envelope = AudioTonePcm.CosineRamp(audibleSamples - 1 - i, fadeOutSamples);

            pcm[i] = (short)(Math.Sin(2 * Math.PI * freq * t) * amp * envelope);
        }

        return pcm;
    }
}

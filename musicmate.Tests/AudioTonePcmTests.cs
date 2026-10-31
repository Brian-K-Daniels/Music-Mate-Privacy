using musicmate.Services;

namespace musicmate.Tests;

public class AudioTonePcmTests
{
    [Fact]
    public void MusicalNote_EndsAtSilence_WithTrailingPad()
    {
        short[] pcm = AudioTonePcm.Build(
            freq: 440,
            durationSeconds: 0.5,
            volume: 0.22f,
            seamlessLoop: false,
            out int audible,
            out int pad);

        Assert.True(audible > 1000);
        Assert.True(pad >= AudioTonePcm.SampleRate * 70 / 1000);
        Assert.Equal(audible + pad, pcm.Length);

        Assert.Equal(0, pcm[audible - 1]);
        Assert.True(pcm.Skip(audible).All(s => s == 0));
        Assert.Contains(pcm.Take(audible), s => Math.Abs((int)s) > 1000);
    }

    [Fact]
    public void MusicalNote_FadeOut_QuieterThanSustain()
    {
        short[] pcm = AudioTonePcm.Build(440, 0.4, 0.5f, seamlessLoop: false, out int audible, out _);
        int window = AudioTonePcm.SampleRate / 50;
        double sustain = Rms(pcm, audible / 2, window);
        double tail = Rms(pcm, audible - window, window);

        Assert.True(sustain > 1000);
        Assert.True(tail < sustain * 0.2);
    }

    private static double Rms(short[] pcm, int start, int count)
    {
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            double s = pcm[start + i];
            sum += s * s;
        }
        return Math.Sqrt(sum / count);
    }

    [Fact]
    public void ShortClick_KeepsBriefPad()
    {
        short[] pcm = AudioTonePcm.Build(880, 0.04, 0.2f, false, out int audible, out int pad);
        Assert.True(pad > 0);
        Assert.True(pad < AudioTonePcm.SampleRate * 0.04);
        Assert.True(pcm.Skip(audible).All(s => s == 0));
    }
}

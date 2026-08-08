using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class WaitingCountInLogicTests
{
    [Theory]
    [InlineData("2/4", 2)]
    [InlineData("3/4", 3)]
    [InlineData("4/4", 4)]
    [InlineData("5/4", 5)]
    [InlineData("2/2", 2)]
    [InlineData("3/8", 3)]
    [InlineData("6/8", 2)]  // compound: dotted-quarter beats
    [InlineData("9/8", 3)]
    [InlineData("12/8", 4)]
    public void GetBeatsPerMeasure_MatchesConductedBeats(string display, int expected)
        => Assert.Equal(expected, WaitingCountInLogic.GetBeatsPerMeasure(display));

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void IsAccentedBeat_OnlyFirstBeatOfMeasure(int beatInMeasure, bool accented)
        => Assert.Equal(accented, WaitingCountInLogic.IsAccentedBeat(beatInMeasure));

    [Fact]
    public void AbsoluteBeatIndex_AccentsEveryMeasureDownbeat()
    {
        const int beats = 4;
        for (long i = 0; i < 16; i++)
        {
            bool accent = WaitingCountInLogic.IsAccentedBeat(i, beats);
            Assert.Equal(i % beats == 0, accent);
        }
    }

    [Fact]
    public void BuildClick_UsesAccentedParamsOnDownbeat()
    {
        var click = WaitingCountInLogic.BuildClick(
            absoluteBeatIndex: 0,
            beatsPerMeasure: 4,
            accentedVolume: 0.5f,
            unaccentedVolume: 0.2f,
            accentedPitchHz: 1760,
            unaccentedPitchHz: 880,
            beatDurationMs: 35,
            tempoBpm: 100);

        Assert.True(click.IsAccented);
        Assert.Equal(0.5f, click.Volume);
        Assert.Equal(1760, click.FrequencyHz);
    }

    [Fact]
    public void BuildClick_UsesUnaccentedParamsOffDownbeat()
    {
        var click = WaitingCountInLogic.BuildClick(
            absoluteBeatIndex: 1,
            beatsPerMeasure: 4,
            accentedVolume: 0.5f,
            unaccentedVolume: 0.2f,
            accentedPitchHz: 1760,
            unaccentedPitchHz: 880,
            beatDurationMs: 35,
            tempoBpm: 100);

        Assert.False(click.IsAccented);
        Assert.Equal(0.2f, click.Volume);
        Assert.Equal(880, click.FrequencyHz);
    }

    [Theory]
    [InlineData(true, true, 0, true)]
    [InlineData(false, true, 0, false)]
    [InlineData(true, false, 0, false)]
    [InlineData(true, true, 1, false)]
    public void ShouldStopForFirstNote(
        bool evaluateCorrect, bool countInActive, int currentNoteIndex, bool expected)
        => Assert.Equal(
            expected,
            WaitingCountInLogic.ShouldStopForFirstNote(evaluateCorrect, countInActive, currentNoteIndex));

    [Fact]
    public void ResolveClickDuration_CapsToBeatFraction()
    {
        double msPerBeat = WaitingCountInLogic.MsPerBeat(180); // fast
        double seconds = WaitingCountInLogic.ResolveClickDurationSeconds(80, msPerBeat);
        Assert.True(seconds * 1000 <= msPerBeat * 0.45 + 0.001);
    }
}

[Collection("SessionPreferences")]
public class WaitingCountInSettingsTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public WaitingCountInSettingsTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void Defaults_AreRestoredAfterFactoryReset()
    {
        WaitingCountInSettings.Enabled = true;
        WaitingCountInSettings.AccentedVolume = 0.9f;
        WaitingCountInSettings.UnaccentedVolume = 0.1f;
        WaitingCountInSettings.AccentedPitchHz = 2000;
        WaitingCountInSettings.UnaccentedPitchHz = 1000;
        WaitingCountInSettings.BeatDurationMs = 60;

        WaitingCountInSettings.ResetToFactoryDefaults();

        Assert.Equal(WaitingCountInSettings.DefaultEnabled, WaitingCountInSettings.Enabled);
        Assert.Equal(WaitingCountInSettings.DefaultAccentedVolume, WaitingCountInSettings.AccentedVolume);
        Assert.Equal(WaitingCountInSettings.DefaultUnaccentedVolume, WaitingCountInSettings.UnaccentedVolume);
        Assert.Equal(WaitingCountInSettings.DefaultAccentedPitchHz, WaitingCountInSettings.AccentedPitchHz);
        Assert.Equal(WaitingCountInSettings.DefaultUnaccentedPitchHz, WaitingCountInSettings.UnaccentedPitchHz);
        Assert.Equal(WaitingCountInSettings.DefaultBeatDurationMs, WaitingCountInSettings.BeatDurationMs);
    }

    [Fact]
    public void Values_PersistAndRestore()
    {
        WaitingCountInSettings.Enabled = true;
        WaitingCountInSettings.AccentedVolume = 0.33f;
        WaitingCountInSettings.UnaccentedVolume = 0.11f;
        WaitingCountInSettings.AccentedPitchHz = 1500;
        WaitingCountInSettings.UnaccentedPitchHz = 900;
        WaitingCountInSettings.BeatDurationMs = 40;

        Assert.True(WaitingCountInSettings.Enabled);
        Assert.Equal(0.33f, WaitingCountInSettings.AccentedVolume, 3);
        Assert.Equal(0.11f, WaitingCountInSettings.UnaccentedVolume, 3);
        Assert.Equal(1500, WaitingCountInSettings.AccentedPitchHz, 0);
        Assert.Equal(900, WaitingCountInSettings.UnaccentedPitchHz, 0);
        Assert.Equal(40, WaitingCountInSettings.BeatDurationMs);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    [InlineData(0.5f, 0.5f)]
    public void ClampVolume(float input, float expected)
        => Assert.Equal(expected, WaitingCountInSettings.ClampVolume(input));

    [Theory]
    [InlineData(100, 440)]
    [InlineData(5000, 4000)]
    [InlineData(880, 880)]
    public void ClampPitchHz(double input, double expected)
        => Assert.Equal(expected, WaitingCountInSettings.ClampPitchHz(input));

    [Theory]
    [InlineData(1, 30)]
    [InlineData(200, 120)]
    [InlineData(55, 55)]
    public void ClampDurationMs(int input, int expected)
        => Assert.Equal(expected, WaitingCountInSettings.ClampDurationMs(input));
}

[Collection("SessionPreferences")]
public class WaitingCountInPlayerTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();
    private readonly FakeCountInClickService _clicks = new();

    public WaitingCountInPlayerTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public async Task Stop_CancelsActiveLoop()
    {
        var player = new WaitingCountInPlayer(_clicks);
        using var cts = new CancellationTokenSource();
        var run = player.RunAsync(120, 4, 0.4f, 0.2f, 1760, 880, 20, cts.Token);
        await Task.Delay(30);
        Assert.True(player.IsActive);
        player.Stop();
        await Task.Delay(50);
        Assert.False(player.IsActive);
        await run;
    }

    [Fact]
    public async Task StartingAgain_DoesNotLeaveDuplicateActiveLoops()
    {
        var player = new WaitingCountInPlayer(_clicks);
        using var cts = new CancellationTokenSource();
        var first = player.RunAsync(180, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);
        await Task.Delay(20);
        var second = player.RunAsync(180, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);
        await Task.Delay(40);
        Assert.True(player.IsActive);
        player.Stop();
        await Task.WhenAll(first, second);
        Assert.False(player.IsActive);
        Assert.True(_clicks.StopCount >= 1);
        Assert.True(_clicks.PlayCount >= 1);
    }

    private sealed class FakeCountInClickService : ICountInClickService
    {
        public int StopCount;
        public int PlayCount;

        public async Task PlayClickAsync(bool accented, int durationMs, float volume, CancellationToken ct)
        {
            Interlocked.Increment(ref PlayCount);
            await Task.Delay(Math.Max(1, durationMs), ct);
        }

        public void Stop() => Interlocked.Increment(ref StopCount);
    }
}

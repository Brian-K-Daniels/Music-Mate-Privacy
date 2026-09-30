using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Tuner metronome tempo and time signature stay on their own preferences.
/// </summary>
[Collection("SessionPreferences")]
public class TunerMetronomeSettingsTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public TunerMetronomeSettingsTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose()
        => SessionPreferences.TestStore = null;

    [Fact]
    public void TimeSignature_FactoryDefault_IsFourFour()
    {
        Assert.Equal("4/4", TunerMetronomeSettings.DefaultTimeSignature);
        Assert.Equal("4/4", TunerMetronomeSettings.LoadTimeSignature());
        Assert.Equal(4, TunerMetronomeSettings.BeatsPerMeasure(null));
    }

    [Fact]
    public void TimeSignature_PersistsAcrossReload()
    {
        TunerMetronomeSettings.SaveTimeSignature("6/8");

        Assert.Equal("6/8", TunerMetronomeSettings.LoadTimeSignature());
        Assert.Equal("6/8", SessionPreferences.Get(TunerMetronomeSettings.TimeSignaturePreferenceKey, ""));
        Assert.Equal(2, TunerMetronomeSettings.BeatsPerMeasure(TunerMetronomeSettings.LoadTimeSignature()));

        TunerMetronomeSettings.SaveTimeSignature("3/4");
        Assert.Equal("3/4", TunerMetronomeSettings.LoadTimeSignature());
    }

    [Fact]
    public void TimeSignature_UnknownValue_FallsBackToFourFour()
    {
        SessionPreferences.Set(TunerMetronomeSettings.TimeSignaturePreferenceKey, "7/8");

        Assert.Equal("4/4", TunerMetronomeSettings.LoadTimeSignature());
    }

    [Fact]
    public void Tempo_FactoryDefault_IsMusicDefaultTempo()
    {
        Assert.Equal(NoteSessionService.DefaultTempo, TunerMetronomeSettings.LoadTempo());
        Assert.Equal(100, TunerMetronomeSettings.LoadTempo());
    }

    [Fact]
    public void Tempo_PersistsAcrossReload()
    {
        TunerMetronomeSettings.SaveTempo(72);

        Assert.Equal(72, TunerMetronomeSettings.LoadTempo());
        Assert.Equal(72, SessionPreferences.Get(TunerMetronomeSettings.TempoPreferenceKey, 0));

        TunerMetronomeSettings.SaveTempo(144);
        Assert.Equal(144, TunerMetronomeSettings.LoadTempo());
    }

    [Fact]
    public void TempoAndTimeSignature_StayIndependentOfMusicPage()
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            Tempo = 80,
            MeterTimeSignature = "3/4",
        };

        TunerMetronomeSettings.SaveTempo(132);
        TunerMetronomeSettings.SaveTimeSignature("5/4");

        Assert.Equal(80, session.Tempo);
        Assert.Equal("3/4", session.MeterTimeSignature);
        Assert.Equal("3/4", session.GetDisplayTimeSignature());
        Assert.Equal(132, TunerMetronomeSettings.LoadTempo());
        Assert.Equal("5/4", TunerMetronomeSettings.LoadTimeSignature());

        session.Tempo = 96;
        session.MeterTimeSignature = "6/8";

        Assert.Equal(132, TunerMetronomeSettings.LoadTempo());
        Assert.Equal("5/4", TunerMetronomeSettings.LoadTimeSignature());
        Assert.Equal(96, SessionPreferences.Get("musicmate.MusicBpm", 0));
        Assert.Equal("6/8", SessionPreferences.Get("musicmate.TimeSignature", ""));
        Assert.Equal(132, SessionPreferences.Get(TunerMetronomeSettings.TempoPreferenceKey, 0));
        Assert.Equal("5/4", SessionPreferences.Get(TunerMetronomeSettings.TimeSignaturePreferenceKey, ""));
    }

    [Fact]
    public void ClearPersisted_RestoresFactoryDefaults()
    {
        TunerMetronomeSettings.SaveTempo(88);
        TunerMetronomeSettings.SaveTimeSignature("9/8");

        TunerMetronomeSettings.ClearPersisted();

        Assert.Equal(NoteSessionService.DefaultTempo, TunerMetronomeSettings.LoadTempo());
        Assert.Equal("4/4", TunerMetronomeSettings.LoadTimeSignature());
    }
}

using musicmate.Services;

namespace musicmate.Tests;

public class TunerVoiceInstrumentTests
{
    [Theory]
    [InlineData("Voice – Soprano")]
    [InlineData("Voice – Mezzo-soprano")]
    [InlineData("Voice – Bass")]
    [InlineData("voice-tenor")]
    public void VoiceInstrument_ResolvesToConcertPitchOnTuner(string instrument)
    {
        Assert.True(TunerReferenceNoteCatalog.UsesConcertPitchOnTuner(instrument));
        Assert.Equal("Concert Pitch", TunerReferenceNoteCatalog.GetTunerInstrumentPickerDisplayName(instrument));
        Assert.Equal(0, TunerReferenceNoteCatalog.GetEffectiveTransposeOffset(instrument));
        Assert.Equal(InstrumentCatalog.Default.Id, TunerReferenceNoteCatalog.GetEffectiveInstrumentProfile(instrument).Id);
    }

    [Fact]
    public void VoicePickerItem_DisplaysConcertPitch_KeepsStoredInstrument()
    {
        const string voice = "Voice – Soprano";
        var item = new TunerInstrumentPickerItem(voice);
        Assert.Equal("Concert Pitch", item.DisplayName);
        Assert.Equal("Concert Pitch", item.ToString());
        Assert.Equal(voice, item.StoredInstrument);
    }

    [Theory]
    [InlineData("Bb Clarinet", -2)]
    [InlineData("F Horn", -7)]
    [InlineData("Concert Pitch", 0)]
    public void NonVoiceInstrument_KeepsTransposition(string instrument, int expectedOffset)
    {
        Assert.False(TunerReferenceNoteCatalog.UsesConcertPitchOnTuner(instrument));
        Assert.Equal(expectedOffset, TunerReferenceNoteCatalog.GetEffectiveTransposeOffset(instrument));
        Assert.Equal(instrument, TunerReferenceNoteCatalog.GetTunerInstrumentPickerDisplayName(instrument));
    }

    [Fact]
    public void EffectiveProfileLookup_DoesNotMutateSessionInstrument()
    {
        const string voice = "Voice – Soprano";
        var session = new NoteSessionService { Instrument = voice };
        string stored = session.Instrument;
        _ = TunerReferenceNoteCatalog.GetEffectiveInstrumentProfile(session.Instrument);
        _ = TunerReferenceNoteCatalog.GetTunerInstrumentPickerDisplayName(session.Instrument);
        Assert.Equal(stored, session.Instrument);
        Assert.Equal("Voice – Soprano", session.InstrumentDisplayName);
    }
}

using musicmate.Services;

namespace musicmate.Tests;

public class TunerPickerLayoutTests
{
    /// <summary>Landscape Tuner instrument/note row (right panel), not a portrait page width.</summary>
    private const double PhoneRowWidth = 360;

    public static TheoryData<string, double> LongInstrumentNames => new()
    {
        { "Soprano Saxophone", 140 },
        { "Eb Alto Saxophone", 128 },
        { "Voice – Mezzo-soprano", 105 },
        { "Concert Pitch", 93 },
        { "Bb Clarinet", 84 },
    };

    [Theory]
    [MemberData(nameof(LongInstrumentNames))]
    public void InstrumentText_FitsPhoneWidthRow(string instrumentName, double textWidth)
    {
        _ = instrumentName;
        var layout = TunerPickerLayout.Allocate(PhoneRowWidth, textWidth, noteTextWidth: 36);
        Assert.True(
            TunerPickerLayout.TextFits(textWidth, layout.InstrumentTextBudget, layout.FontSize),
            $"Instrument text should fit inner budget {layout.InstrumentTextBudget} at font {layout.FontSize}");
    }

    [Theory]
    [InlineData(41)]
    [InlineData(52)]
    [InlineData(64)]
    public void NoteText_FitsPhoneWidthRow(double noteTextWidth)
    {
        var layout = TunerPickerLayout.Allocate(PhoneRowWidth, instrumentTextWidth: 90, noteTextWidth);
        Assert.True(
            TunerPickerLayout.TextFits(noteTextWidth, layout.NoteTextBudget, layout.FontSize),
            $"Note text should fit inner budget {layout.NoteTextBudget} at font {layout.FontSize}");
    }

    [Fact]
    public void LongInstrumentAndNote_ShrinkFontBeforeClipping()
    {
        var layout = TunerPickerLayout.Allocate(
            rowWidth: PhoneRowWidth,
            instrumentTextWidth: 180,
            noteTextWidth: 90);
        Assert.True(layout.FontSize < TunerPickerLayout.BaseFontSize);
        Assert.True(TunerPickerLayout.TextFits(180, layout.InstrumentTextBudget, layout.FontSize));
        Assert.True(TunerPickerLayout.TextFits(90, layout.NoteTextBudget, layout.FontSize));
    }

    [Fact]
    public void VoiceDisplayName_ConcertPitch_FitsTypicalPhoneRow()
    {
        var layout = TunerPickerLayout.Allocate(
            PhoneRowWidth,
            instrumentTextWidth: 105,
            noteTextWidth: 52);
        Assert.True(TunerPickerLayout.TextFits(105, layout.InstrumentTextBudget, layout.FontSize));
    }
}

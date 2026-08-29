using musicmate.Services;

namespace musicmate.Tests;

public class TunerPickerLayoutTests
{
    private const double PhoneRowWidth = 220;

    public static TheoryData<string, double> LongInstrumentNames => new()
    {
        { "Soprano Saxophone", 96 },
        { "Eb Alto Saxophone", 88 },
        { "Voice – Mezzo-soprano", 72 },
        { "Concert Pitch", 64 },
        { "Bb Clarinet", 58 },
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
    [InlineData(28)]
    [InlineData(36)]
    [InlineData(44)]
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
            instrumentTextWidth: 100,
            noteTextWidth: 48);
        Assert.True(layout.FontSize < TunerPickerLayout.BaseFontSize);
        Assert.True(TunerPickerLayout.TextFits(100, layout.InstrumentTextBudget, layout.FontSize));
        Assert.True(TunerPickerLayout.TextFits(48, layout.NoteTextBudget, layout.FontSize));
    }

    [Fact]
    public void VoiceDisplayName_ConcertPitch_FitsTypicalPhoneRow()
    {
        var layout = TunerPickerLayout.Allocate(
            PhoneRowWidth,
            instrumentTextWidth: 72,
            noteTextWidth: 36);
        Assert.True(TunerPickerLayout.TextFits(72, layout.InstrumentTextBudget, layout.FontSize));
    }
}

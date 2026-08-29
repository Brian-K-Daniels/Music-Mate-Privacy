namespace musicmate.Services;

/// <summary>
/// Width/font allocation for the Tuner Instrument and Note picker row.
/// </summary>
public static class TunerPickerLayout
{
    public const float BaseFontSize = 11f;
    public const float MinimumFontSize = 6.5f;
    /// <summary>Border chrome horizontal padding (left + right), matches TunerPickerChrome Padding.</summary>
    public const double ChromeHorizontalPadding = 12;
    public const double SpinnerChevron = 16;
    /// <summary>Skia advance vs MAUI bold label rendering.</summary>
    public const double TextRenderSlack = 3;
    public const double InstrumentLabelWidth = 26;
    /// <summary>"Note" label plus its Grid.Column left margin.</summary>
    public const double NoteLabelWidth = 40;
    public const double ColumnSpacing = 12;
    public const double InstrumentShare = 0.60;

    public readonly record struct ChromeLayout(
        double InstrumentChromeMin,
        double NoteChromeMin,
        float FontSize,
        double InstrumentBudget,
        double NoteBudget,
        double InstrumentTextBudget,
        double NoteTextBudget);

    /// <summary>
    /// Computes star-column minimum widths and a font size so both labels fit inside their columns.
    /// </summary>
    public static ChromeLayout Allocate(
        double rowWidth,
        double instrumentTextWidth,
        double noteTextWidth,
        float baseFontSize = BaseFontSize)
    {
        rowWidth = Math.Max(rowWidth, 200);
        double available = Math.Max(0, rowWidth - InstrumentLabelWidth - NoteLabelWidth - ColumnSpacing);
        double instrumentBudget = available * InstrumentShare;
        double noteBudget = available * (1 - InstrumentShare);
        double instrumentTextBudget = Math.Max(0, instrumentBudget - ChromeHorizontalPadding - SpinnerChevron);
        double noteTextBudget = Math.Max(0, noteBudget - ChromeHorizontalPadding - SpinnerChevron);

        float fontSize = baseFontSize;
        while (fontSize > MinimumFontSize + 0.01f)
        {
            double scale = fontSize / baseFontSize;
            bool instFits = instrumentTextWidth * scale <= instrumentTextBudget + TextRenderSlack;
            bool noteFits = noteTextWidth * scale <= noteTextBudget + TextRenderSlack;
            if (instFits && noteFits)
                break;
            fontSize -= 0.5f;
        }

        return new ChromeLayout(
            Math.Max(48, instrumentBudget),
            Math.Max(40, noteBudget),
            fontSize,
            instrumentBudget,
            noteBudget,
            instrumentTextBudget,
            noteTextBudget);
    }

    public static bool TextFits(double textWidth, double textBudget, float fontSize, float baseFontSize = BaseFontSize)
    {
        double scale = fontSize / baseFontSize;
        return textWidth * scale <= textBudget + TextRenderSlack;
    }
}

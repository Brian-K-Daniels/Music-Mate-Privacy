using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Conducted-beat positions within a measure for visual conductor cues.
/// Simple meters use the written beat unit; compound 6/8, 9/8, and 12/8 use dotted-quarter beats.
/// </summary>
public static class ConductorBeatHelper
{
    /// <summary>Dotted-quarter beat length expressed as quarter-note beats.</summary>
    public const double CompoundConductedBeatQuarterValue = 1.5;

    public static bool TryParseDisplayTimeSignature(string? display, out TimeSignature timeSignature)
        => TimeSignature.TryParse(display, out timeSignature);

    public static bool IsCompoundConductedMeter(TimeSignature timeSignature)
        => timeSignature.BeatUnit == NoteDuration.Eighth
           && timeSignature.Beats >= 6
           && timeSignature.Beats % 3 == 0;

    public static int GetConductedBeatCount(TimeSignature timeSignature)
        => IsCompoundConductedMeter(timeSignature)
            ? timeSignature.Beats / 3
            : timeSignature.Beats;

    public static double GetConductedBeatDurationInQuarterBeats(TimeSignature timeSignature)
        => IsCompoundConductedMeter(timeSignature)
            ? CompoundConductedBeatQuarterValue
            : timeSignature.BeatUnit.ToBeatValue();

    /// <summary>
    /// Beat offsets from the start of one measure, in quarter-note beats.
    /// </summary>
    public static IReadOnlyList<double> GetConductedBeatOffsetsInMeasure(TimeSignature timeSignature)
    {
        int count = GetConductedBeatCount(timeSignature);
        double step = GetConductedBeatDurationInQuarterBeats(timeSignature);
        var offsets = new double[count];
        for (int i = 0; i < count; i++)
            offsets[i] = i * step;
        return offsets;
    }

    /// <summary>
    /// Maps a relative beat position to the conducted-beat start within its measure.
    /// </summary>
    public static double GetConductedBeatStartForRelativeBeat(
        double relativeBeat,
        TimeSignature timeSignature,
        IReadOnlyList<double> sortedMeasureStartBeats)
    {
        double measureStart = GetMeasureStartBeat(relativeBeat, sortedMeasureStartBeats);
        double beatInMeasure = relativeBeat - measureStart;
        double conductedStep = GetConductedBeatDurationInQuarterBeats(timeSignature);
        int conductedIndex = (int)Math.Floor(beatInMeasure / conductedStep + 1e-6);
        conductedIndex = Math.Clamp(conductedIndex, 0, GetConductedBeatCount(timeSignature) - 1);
        return measureStart + conductedIndex * conductedStep;
    }

    public static double GetMeasureStartBeat(double relativeBeat, IReadOnlyList<double> sortedMeasureStartBeats)
    {
        double start = 0.0;
        foreach (double boundary in sortedMeasureStartBeats.OrderBy(b => b))
        {
            if (relativeBeat < boundary - 1e-6)
                return start;
            start = boundary;
        }

        return start;
    }

    /// <summary>
    /// Converts a beat offset inside a measure to the same horizontal lane used for time-based layout.
    /// </summary>
    public static float BeatOffsetToXInMeasure(
        float measureLeft,
        float measureRight,
        double beatOffsetInMeasure,
        double measureBeatsInQuarters,
        float barLeftPadding,
        float barRightPaddingFactor = 0.5f)
    {
        if (measureBeatsInQuarters < 1e-9)
            measureBeatsInQuarters = 1.0;

        float laneLeft = measureLeft + barLeftPadding;
        float laneRight = measureRight - barLeftPadding * barRightPaddingFactor;
        float laneSpan = Math.Max(8f, laneRight - laneLeft);
        float frac = (float)Math.Clamp(beatOffsetInMeasure / measureBeatsInQuarters, 0.0, 1.0);
        return laneLeft + frac * laneSpan;
    }
}

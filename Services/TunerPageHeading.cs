namespace musicmate.Services;

/// <summary>
/// The Tuner title bar shows one heading. The status line must not repeat it.
/// </summary>
public static class TunerPageHeading
{
    public const string Title = "Tuner";
    public const double TitleFontSize = 28;

    public static string StatusBesideHeading(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return string.Empty;

        return string.Equals(status.Trim(), Title, StringComparison.Ordinal)
            ? string.Empty
            : status;
    }
}

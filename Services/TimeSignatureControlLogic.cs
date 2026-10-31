using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Pure helpers for the Music-page time-signature selector.
/// Option list is <see cref="TimeSignature.CommonDisplayOptions"/> — the single source of truth.
/// Authoritative value remains <see cref="NoteSessionService.MeterTimeSignature"/>.
/// </summary>
public static class TimeSignatureControlLogic
{
    /// <summary>Same meters formerly offered in Settings → Music.</summary>
    public static IReadOnlyList<string> Options => TimeSignature.CommonDisplayOptions;

    /// <summary>
    /// True when <paramref name="display"/> is one of <see cref="Options"/>.
    /// </summary>
    public static bool IsAllowed(string? display)
        => !string.IsNullOrWhiteSpace(display)
           && Options.Contains(display, StringComparer.Ordinal);

    /// <summary>
    /// Returns <paramref name="selected"/> when it is an allowed meter; otherwise <c>null</c>.
    /// </summary>
    public static string? NormalizeSelection(string? selected)
        => IsAllowed(selected) ? selected : null;

    /// <summary>True when applying <paramref name="selected"/> would change the current meter.</summary>
    public static bool WouldChange(string? current, string? selected)
    {
        string? next = NormalizeSelection(selected);
        if (next == null)
            return false;
        string cur = string.IsNullOrWhiteSpace(current) ? "4/4" : current.Trim();
        return !string.Equals(cur, next, StringComparison.Ordinal);
    }
}

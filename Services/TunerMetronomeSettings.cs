using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Tuner metronome tempo and time signature. Stored separately from the Music page
/// tempo (<c>musicmate.MusicBpm</c>) and meter (<c>musicmate.TimeSignature</c>).
/// </summary>
public static class TunerMetronomeSettings
{
    public const string TempoPreferenceKey = "musicmate.TunerTempo";
    public const string TimeSignaturePreferenceKey = "musicmate.TunerMetronomeTimeSignature";

    public const int DefaultTempo = NoteSessionService.DefaultTempo;
    public const string DefaultTimeSignature = "4/4";

    public static int LoadTempo()
    {
        int saved = SessionPreferences.Get(TempoPreferenceKey, 0);
        if (saved >= NoteSessionService.MinTempo && saved <= NoteSessionService.MaxTempo)
            return saved;
        return DefaultTempo;
    }

    public static void SaveTempo(int bpm)
    {
        SessionPreferences.Set(
            TempoPreferenceKey,
            Math.Clamp(bpm, NoteSessionService.MinTempo, NoteSessionService.MaxTempo));
    }

    public static string LoadTimeSignature()
    {
        string saved = SessionPreferences.Get(TimeSignaturePreferenceKey, DefaultTimeSignature);
        return TimeSignatureControlLogic.NormalizeSelection(saved) ?? DefaultTimeSignature;
    }

    public static void SaveTimeSignature(string? display)
    {
        SessionPreferences.Set(
            TimeSignaturePreferenceKey,
            TimeSignatureControlLogic.NormalizeSelection(display) ?? DefaultTimeSignature);
    }

    public static int BeatsPerMeasure(string? display)
        => WaitingCountInLogic.GetBeatsPerMeasure(
            TimeSignatureControlLogic.NormalizeSelection(display) ?? DefaultTimeSignature);

    public static void ClearPersisted()
    {
        SessionPreferences.Remove(TempoPreferenceKey);
        SessionPreferences.Remove(TimeSignaturePreferenceKey);
    }
}

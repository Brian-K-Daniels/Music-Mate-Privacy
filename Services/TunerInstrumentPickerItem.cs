namespace musicmate.Services;

/// <summary>
/// Tuner instrument picker entry: shows Tuner-local display text (voice → Concert Pitch)
/// while preserving the stored <see cref="NoteSessionService.Instrument"/> value.
/// </summary>
public sealed record TunerInstrumentPickerItem(string StoredInstrument)
{
    public string DisplayName => TunerReferenceNoteCatalog.GetTunerInstrumentPickerDisplayName(StoredInstrument);

    public override string ToString() => DisplayName;

    public static IReadOnlyList<TunerInstrumentPickerItem> BuildAll()
        => NoteSessionService.InstrumentOptions
            .Select(stored => new TunerInstrumentPickerItem(stored))
            .ToArray();
}

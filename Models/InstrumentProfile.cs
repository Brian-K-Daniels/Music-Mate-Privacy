namespace musicmate.Models
{
    public sealed record InstrumentProfile(
        string Id,
        string DisplayName,
        string InstrumentKey,
        string LegacyInstrumentValue,
        int TransposeOffset,
        string PracticalLowestNote,
        string PracticalHighestNote,
        IReadOnlyList<string> Aliases);
}

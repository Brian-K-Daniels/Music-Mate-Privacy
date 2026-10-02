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
        IReadOnlyList<string> Aliases,
        Clef DefaultClef = Clef.Treble,
        IReadOnlyList<Clef>? SupportedClefs = null)
    {
        /// <summary>
        /// Clefs this instrument may show. The first is <see cref="DefaultClef"/>.
        /// A single entry means the clef is not tappable.
        /// </summary>
        public IReadOnlyList<Clef> NotationClefs { get; } =
            BuildNotationClefs(DefaultClef, SupportedClefs);

        public bool CanToggleNotationClef => NotationClefs.Count > 1;

        public bool AllowsNotationClef(Clef clef) => NotationClefs.Contains(clef);

        private static IReadOnlyList<Clef> BuildNotationClefs(Clef defaultClef, IReadOnlyList<Clef>? supported)
        {
            if (supported is not { Count: > 0 })
                return new[] { defaultClef };

            var clefs = new List<Clef> { defaultClef };
            foreach (var clef in supported)
            {
                if (!clefs.Contains(clef))
                    clefs.Add(clef);
            }

            return clefs;
        }
    }
}

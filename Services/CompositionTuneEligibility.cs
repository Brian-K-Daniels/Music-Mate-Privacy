using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Eligible practice tunes for By Level composition, based on session constraints.
    /// </summary>
    public static class CompositionTuneEligibility
    {
        private static readonly HashSet<string> PremiumOnlyTuneTitles =
            new(StringComparer.Ordinal);

        public static CompositionTuneContext CreateContext(
            int level,
            string instrument,
            string lowestNote,
            string highestNote,
            bool isPremiumUser)
            => new(level, instrument, lowestNote, highestNote, isPremiumUser);

        public static CompositionTuneContext FromSession(NoteSessionService session, int level)
            => CreateContext(
                level,
                session.InstrumentKey,
                session.LowestNote,
                session.HighestNote,
                StatusService.Instance.IsPremiumUser);

        public static IReadOnlyList<PracticeTune> GetEligibleTunes(CompositionTuneContext context)
            => TuneLibrary.All
                .Where(tune => IsEligible(tune, context))
                .ToArray();

        public static IReadOnlyList<string> GetEligibleTuneTitles(CompositionTuneContext context)
            => GetEligibleTunes(context).Select(t => t.Title).ToArray();

        public static string BuildContextKey(
            CompositionTuneContext context,
            IReadOnlyList<string> eligibleTitles)
        {
            var titles = eligibleTitles.OrderBy(t => t, StringComparer.Ordinal);
            return string.Join('|',
                context.Level,
                context.Instrument,
                context.LowestNote,
                context.HighestNote,
                context.IsPremiumUser ? "1" : "0",
                TuneLibrary.All.Count,
                string.Join(',', titles));
        }

        public static bool IsEligible(PracticeTune tune, CompositionTuneContext context)
        {
            if (PremiumOnlyTuneTitles.Contains(tune.Title) && !context.IsPremiumUser)
                return false;

            int minMidi = NoteSessionService.NoteNameToMidi(context.LowestNote);
            int maxMidi = NoteSessionService.NoteNameToMidi(context.HighestNote);
            if (minMidi < 0 || maxMidi < minMidi)
                return true;

            foreach (var note in tune.AllNotes)
            {
                if (note.IsRest)
                    continue;

                if (note.MidiNumber < minMidi || note.MidiNumber > maxMidi)
                    return false;
            }

            return true;
        }
    }

    public sealed record CompositionTuneContext(
        int Level,
        string Instrument,
        string LowestNote,
        string HighestNote,
        bool IsPremiumUser);
}

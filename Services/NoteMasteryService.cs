using musicmate.Diagnostics;
using musicmate.Models;
using musicmate.ViewModels;

namespace musicmate.Services;

/// <summary>
/// Builds and caches Note Mastery display data for the current written range.
/// Queries note stats once per open/refresh; invalidates with the note-statistics cache.
/// </summary>
public sealed class NoteMasteryService
{
    private readonly NoteDatabase _noteDatabase;
    private readonly NoteAttemptDatabase _attemptDatabase;
    private readonly NoteSessionService _session;
    private readonly object _lock = new();
    private NoteMasterySnapshot? _cached;

    public NoteMasteryService(
        NoteDatabase noteDatabase,
        NoteAttemptDatabase attemptDatabase,
        NoteSessionService session)
    {
        _noteDatabase = noteDatabase;
        _attemptDatabase = attemptDatabase;
        _session = session;
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _cached = null;
        }
        DebugLog.WriteLine(DebugLogCategory.Statistics, "[NoteMastery] Cache invalidated");
    }

    public async Task<NoteMasterySnapshot> GetSnapshotAsync(bool forceRefresh = false)
    {
        await _noteDatabase.InitializeAsync();
        var fingerprint = await _noteDatabase.GetStatisticsFingerprintAsync();

        lock (_lock)
        {
            if (!forceRefresh
                && _cached is not null
                && _cached.Fingerprint.Matches(fingerprint)
                && RangeMatchesCache(_cached))
            {
                return _cached;
            }
        }

        var snapshot = await BuildSnapshotAsync(fingerprint);

        lock (_lock)
        {
            _cached = snapshot;
        }

        DebugLog.WriteLine(
            DebugLogCategory.Statistics,
            $"[NoteMastery] Loaded {snapshot.TotalCount} notes " +
            $"({snapshot.MasteredCount} mastered) instrument={snapshot.InstrumentDisplayName}");

        return snapshot;
    }

    /// <summary>
    /// Chromatic written notes from lowest to highest inclusive, spelled with the same
    /// key/scale rules as the Music page (one entry per MIDI).
    /// </summary>
    public static IReadOnlyList<(string WrittenName, int Midi)> BuildWrittenNoteRange(
        string lowestNote,
        string highestNote,
        string key,
        string scale = "Major")
    {
        int lo = NoteSessionService.NoteNameToMidi(lowestNote);
        int hi = NoteSessionService.NoteNameToMidi(highestNote);
        if (lo < 0 || hi < 0 || lo > hi)
            return Array.Empty<(string, int)>();

        var list = new List<(string, int)>(hi - lo + 1);
        for (int midi = lo; midi <= hi; midi++)
            list.Add((NoteSessionService.SpellWrittenPitch(midi, key, scale), midi));
        return list;
    }

    private bool RangeMatchesCache(NoteMasterySnapshot cached)
        => string.Equals(cached.LowestNote, _session.LowestNote, StringComparison.OrdinalIgnoreCase)
           && string.Equals(cached.HighestNote, _session.HighestNote, StringComparison.OrdinalIgnoreCase)
           && string.Equals(cached.Key, _session.Key, StringComparison.OrdinalIgnoreCase)
           && string.Equals(cached.Scale, _session.GenerationScale, StringComparison.OrdinalIgnoreCase)
           && string.Equals(
               cached.InstrumentDisplayName,
               _session.InstrumentDisplayName,
               StringComparison.OrdinalIgnoreCase);

    private async Task<NoteMasterySnapshot> BuildSnapshotAsync(StatisticsDbFingerprint fingerprint)
    {
        var statsList = await _noteDatabase.GetAllAsync() ?? new List<NoteStat>();
        var byName = statsList
            .Where(s => !string.IsNullOrWhiteSpace(s.WrittenName))
            .GroupBy(s => s.WrittenName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byMidi = IndexStatsByMidi(byName.Values);

        Dictionary<string, DateTime> lastByNote = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, DateTime> lastByMidi = new();
        try
        {
            await _attemptDatabase.InitializeAsync();
            var attempts = await _attemptDatabase.GetAllAsync() ?? new List<NoteAttempt>();
            string instrument = _session.Instrument;
            foreach (var a in attempts)
            {
                if (a.IsRest
                    || string.IsNullOrWhiteSpace(a.WrittenNoteName)
                    || !InstrumentsMatch(a.Instrument, instrument))
                    continue;

                if (!lastByNote.TryGetValue(a.WrittenNoteName, out var prevName)
                    || a.DateTime > prevName)
                {
                    lastByNote[a.WrittenNoteName] = a.DateTime;
                }

                int attemptMidi = a.MidiNumber > 0
                    ? a.MidiNumber
                    : NoteSessionService.NoteNameToMidi(a.WrittenNoteName);
                if (attemptMidi <= 0)
                    continue;

                if (!lastByMidi.TryGetValue(attemptMidi, out var prevMidi)
                    || a.DateTime > prevMidi)
                {
                    lastByMidi[attemptMidi] = a.DateTime;
                }
            }
        }
        catch
        {
            // Attempt DB is optional for mastery display; leave last-practiced empty.
        }

        string scale = _session.GenerationScale;
        var range = BuildWrittenNoteRange(
            _session.LowestNote, _session.HighestNote, _session.Key, scale);
        var instrumentName = _session.InstrumentDisplayName;
        var items = new List<NoteMasteryItemViewModel>(range.Count);

        foreach (var (name, midi) in range)
        {
            var stat = FindStatForDisplay(byName, byMidi, name, midi);
            var state = NoteMasteryClassifier.Classify(stat, _session);

            DateTime? lastUtc = null;
            if (lastByNote.TryGetValue(name, out var last)
                || lastByMidi.TryGetValue(midi, out last))
            {
                lastUtc = DateTime.SpecifyKind(last, DateTimeKind.Utc);
            }

            items.Add(NoteMasteryItemViewModel.FromStat(
                name, midi, instrumentName, stat, state, lastUtc));
        }

        return new NoteMasterySnapshot
        {
            Notes = items,
            InstrumentDisplayName = instrumentName,
            Key = _session.Key,
            Scale = scale,
            LowestNote = _session.LowestNote,
            HighestNote = _session.HighestNote,
            PreferBassClef = PrefersBassClef(_session.CurrentInstrumentProfile.Id),
            ComputedUtc = DateTime.UtcNow,
            Fingerprint = fingerprint,
        };
    }

    private static Dictionary<int, List<NoteStat>> IndexStatsByMidi(IEnumerable<NoteStat> stats)
    {
        var byMidi = new Dictionary<int, List<NoteStat>>();
        foreach (var stat in stats)
        {
            int midi = NoteSessionService.NoteNameToMidi(stat.WrittenName);
            if (!byMidi.TryGetValue(midi, out var list))
                byMidi[midi] = list = new List<NoteStat>();
            list.Add(stat);
        }
        return byMidi;
    }

    /// <summary>
    /// Prefer an exact written-name match; otherwise attach the richest enharmonic
    /// history for the same MIDI without rewriting stored rows.
    /// </summary>
    internal static NoteStat? FindStatForDisplay(
        IReadOnlyDictionary<string, NoteStat> byName,
        string displayName,
        int midi)
        => FindStatForDisplay(byName, IndexStatsByMidi(byName.Values), displayName, midi);

    internal static NoteStat? FindStatForDisplay(
        IReadOnlyDictionary<string, NoteStat> byName,
        IReadOnlyDictionary<int, List<NoteStat>> byMidi,
        string displayName,
        int midi)
    {
        if (byName.TryGetValue(displayName, out var exact))
            return exact;

        if (!byMidi.TryGetValue(midi, out var candidates) || candidates.Count == 0)
            return null;

        NoteStat? best = null;
        int bestAttempts = -1;
        int bestStreak = -1;
        foreach (var stat in candidates)
        {
            int attempts = NoteMasteryClassifier.TotalAttempts(stat);
            if (attempts > bestAttempts
                || (attempts == bestAttempts && stat.Streak > bestStreak))
            {
                best = stat;
                bestAttempts = attempts;
                bestStreak = stat.Streak;
            }
        }

        return best;
    }

    private static bool InstrumentsMatch(string attemptInstrument, string sessionInstrument)
    {
        if (string.Equals(attemptInstrument, sessionInstrument, StringComparison.OrdinalIgnoreCase))
            return true;
        var a = InstrumentCatalog.Resolve(attemptInstrument).Id;
        var b = InstrumentCatalog.Resolve(sessionInstrument).Id;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PrefersBassClef(string instrumentId)
        => instrumentId is "double-bass";
}

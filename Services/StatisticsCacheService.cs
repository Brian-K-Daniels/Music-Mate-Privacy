#nullable enable
using musicmate.Utilities;

namespace musicmate.Services;

/// <summary>Which statistics database tab is active on the Statistics page.</summary>
public enum StatisticsDatabaseKind
{
    Note,
    Session,
    ChildResults
}

/// <summary>Lightweight DB signature used to detect when cached statistics are stale.</summary>
public readonly record struct StatisticsDbFingerprint(int RowCount, int Revision)
{
    public bool Matches(StatisticsDbFingerprint other) =>
        RowCount == other.RowCount && Revision == other.Revision;
}

/// <summary>Cached note-statistics payload (data only — no UI state).</summary>
public sealed class NoteStatisticsCacheData
{
    public required IReadOnlyList<NoteStat> Stats { get; init; }
}

/// <summary>Cached session-statistics payload (data only — no UI state).</summary>
public sealed class SessionStatisticsCacheData
{
    public required IReadOnlyList<SessionStat> Stats { get; init; }
}

/// <summary>One cached statistics snapshot for a single database kind.</summary>
public sealed class StatisticsCacheEntry
{
    public required int CacheVersion { get; init; }
    public required StatisticsDatabaseKind Kind { get; init; }
    public required StatisticsDbFingerprint Fingerprint { get; init; }
    public required DateTime LastComputedUtc { get; init; }
    public NoteStatisticsCacheData? NoteData { get; init; }
    public SessionStatisticsCacheData? SessionData { get; init; }
}

/// <summary>
/// In-memory cache for Statistics page data. Bump <see cref="CacheVersion"/> when
/// calculation or schema logic changes.
/// </summary>
public sealed class StatisticsCacheService
{
  /// <summary>Increment when statistics calculation logic or displayed schema changes.</summary>
    public const int CacheVersion = 2;

    private readonly object _lock = new();
    private readonly Dictionary<StatisticsDatabaseKind, StatisticsCacheEntry> _entries = new();

    public void Invalidate(StatisticsDatabaseKind? kind = null)
    {
        lock (_lock)
        {
            if (kind is null)
                _entries.Clear();
            else
                _entries.Remove(kind.Value);
        }
        Utils.Log($"[StatisticsCacheService] Invalidated {(kind?.ToString() ?? "all")}");
    }

    public void InvalidateNoteStats() => Invalidate(StatisticsDatabaseKind.Note);

    public void InvalidateSessionStats() => Invalidate(StatisticsDatabaseKind.Session);

    public bool TryGet(StatisticsDatabaseKind kind, out StatisticsCacheEntry? entry)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(kind, out var cached))
            {
                entry = cached;
                return true;
            }
        }

        entry = null;
        return false;
    }

    public void Store(StatisticsCacheEntry entry)
    {
        lock (_lock)
        {
            _entries[entry.Kind] = entry;
        }
    }

    public static bool IsValid(StatisticsCacheEntry entry, StatisticsDbFingerprint fingerprint) =>
        entry.CacheVersion == CacheVersion && entry.Fingerprint.Matches(fingerprint);

    public static StatisticsDatabaseKind MapSelectedDatabase(string selectedDatabase) =>
        selectedDatabase switch
        {
            "Session" => StatisticsDatabaseKind.Session,
            "Child Results" => StatisticsDatabaseKind.ChildResults,
            _ => StatisticsDatabaseKind.Note
        };

    public static async Task<StatisticsDbFingerprint> GetFingerprintAsync(
        StatisticsDatabaseKind kind,
        NoteDatabase noteDatabase,
        SessionDatabase sessionDatabase)
    {
        return kind switch
        {
            StatisticsDatabaseKind.Note => await noteDatabase.GetStatisticsFingerprintAsync(),
            StatisticsDatabaseKind.Session => await sessionDatabase.GetStatisticsFingerprintAsync(),
            _ => new StatisticsDbFingerprint(0, 0)
        };
    }

    public static async Task<StatisticsCacheEntry> ComputeAsync(
        StatisticsDatabaseKind kind,
        NoteDatabase noteDatabase,
        SessionDatabase sessionDatabase)
    {
        var computedUtc = DateTime.UtcNow;

        return kind switch
        {
            StatisticsDatabaseKind.Note => await ComputeNoteAsync(noteDatabase, computedUtc),
            StatisticsDatabaseKind.Session => await ComputeSessionAsync(sessionDatabase, computedUtc),
            _ => new StatisticsCacheEntry
            {
                CacheVersion = CacheVersion,
                Kind = kind,
                Fingerprint = new StatisticsDbFingerprint(0, 0),
                LastComputedUtc = computedUtc
            }
        };
    }

    private static async Task<StatisticsCacheEntry> ComputeNoteAsync(
        NoteDatabase noteDatabase,
        DateTime computedUtc)
    {
        var noteData = await StatisticsDataLoader.LoadNoteStatsAsync(noteDatabase);
        int revision = noteData.Stats.Sum(s => s.Correct + s.Wrong);
        return new StatisticsCacheEntry
        {
            CacheVersion = CacheVersion,
            Kind = StatisticsDatabaseKind.Note,
            Fingerprint = new StatisticsDbFingerprint(noteData.Stats.Count, revision),
            LastComputedUtc = computedUtc,
            NoteData = noteData
        };
    }

    private static async Task<StatisticsCacheEntry> ComputeSessionAsync(
        SessionDatabase sessionDatabase,
        DateTime computedUtc)
    {
        var sessionData = await StatisticsDataLoader.LoadSessionStatsAsync(sessionDatabase);
        int revision = sessionData.Stats.Count > 0
            ? sessionData.Stats.Max(s => s.Id)
            : 0;
        return new StatisticsCacheEntry
        {
            CacheVersion = CacheVersion,
            Kind = StatisticsDatabaseKind.Session,
            Fingerprint = new StatisticsDbFingerprint(sessionData.Stats.Count, revision),
            LastComputedUtc = computedUtc,
            SessionData = sessionData
        };
    }
}

/// <summary>Loads statistics from databases (same ordering as the pre-cache ViewModel).</summary>
public static class StatisticsDataLoader
{
    public static async Task<NoteStatisticsCacheData> LoadNoteStatsAsync(NoteDatabase noteDatabase)
    {
        var stats = await noteDatabase.GetAllAsync().ConfigureAwait(false);
        var ordered = stats.OrderBy(s => s.PercentCorrect).ToList();
        return new NoteStatisticsCacheData
        {
            Stats = CloneNoteStats(ordered)
        };
    }

    public static async Task<SessionStatisticsCacheData> LoadSessionStatsAsync(SessionDatabase sessionDatabase)
    {
        var stats = await sessionDatabase.GetAllAsync().ConfigureAwait(false);
        return new SessionStatisticsCacheData
        {
            Stats = CloneSessionStats(stats)
        };
    }

    private static List<NoteStat> CloneNoteStats(IEnumerable<NoteStat> source)
    {
        var list = new List<NoteStat>();
        foreach (var stat in source)
        {
            list.Add(new NoteStat
            {
                WrittenName = stat.WrittenName,
                Correct = stat.Correct,
                Wrong = stat.Wrong,
                PitchCorrectCount = stat.PitchCorrectCount,
                PitchWrongCount = stat.PitchWrongCount,
                TimingCorrectCount = stat.TimingCorrectCount,
                TimingWrongCount = stat.TimingWrongCount,
                OverallCorrectCount = stat.OverallCorrectCount,
                OverallWrongCount = stat.OverallWrongCount,
                RestCorrectCount = stat.RestCorrectCount,
                RestWrongCount = stat.RestWrongCount,
                Accidental = stat.Accidental,
                NoteLetter = stat.NoteLetter,
                Octave = stat.Octave,
                MsAverage = stat.MsAverage,
                MsCount = stat.MsCount,
                Streak = stat.Streak,
                Mastered = stat.Mastered
            });
        }

        return list;
    }

    private static List<SessionStat> CloneSessionStats(IEnumerable<SessionStat> source)
    {
        var list = new List<SessionStat>();
        foreach (var stat in source)
        {
            list.Add(new SessionStat
            {
                Id = stat.Id,
                Key = stat.Key,
                Tune = stat.Tune,
                Instrument = stat.Instrument,
                Dt = stat.Dt,
                What = stat.What,
                Hi = stat.Hi,
                Lo = stat.Lo,
                Pc = stat.Pc,
                PcRaw = stat.PcRaw,
                Tp = stat.Tp,
                Ts = stat.Ts,
                Level = stat.Level,
                Pch = stat.Pch,
                Tmg = stat.Tmg,
                Ovrl = stat.Ovrl,
                PitchRightCount = stat.PitchRightCount,
                PitchWrongCount = stat.PitchWrongCount,
                TimingRightCount = stat.TimingRightCount,
                TimingWrongCount = stat.TimingWrongCount,
                OverallRightCount = stat.OverallRightCount,
                OverallWrongCount = stat.OverallWrongCount,
                RestRightCount = stat.RestRightCount,
                RestWrongCount = stat.RestWrongCount,
                Rand = stat.Rand,
                AccPct = stat.AccPct
            });
        }

        return list;
    }
}

using musicmate.Services;

namespace musicmate.Tests;

public class StatisticsCacheServiceTests : IDisposable
{
    static StatisticsCacheServiceTests()
    {
        SQLitePCL.Batteries.Init();
    }

    private readonly string _noteDbPath;
    private readonly string _sessionDbPath;
    private readonly NoteDatabase _noteDatabase;
    private readonly SessionDatabase _sessionDatabase;
    private readonly StatisticsCacheService _cache = new();

    public StatisticsCacheServiceTests()
    {
        _noteDbPath = Path.Combine(Path.GetTempPath(), $"mm_note_stats_{Guid.NewGuid():N}.db3");
        _sessionDbPath = Path.Combine(Path.GetTempPath(), $"mm_session_stats_{Guid.NewGuid():N}.db3");
        _noteDatabase = new NoteDatabase(_noteDbPath);
        _sessionDatabase = new SessionDatabase(_sessionDbPath);
    }

    public void Dispose()
    {
        try { if (File.Exists(_noteDbPath)) File.Delete(_noteDbPath); } catch { }
        try { if (File.Exists(_sessionDbPath)) File.Delete(_sessionDbPath); } catch { }
    }

    [Fact]
    public async Task FirstLoad_ComputesAndCachesSessionStats()
    {
        await _sessionDatabase.InitializeAsync();
        await _sessionDatabase.InsertAsync(new SessionStat
        {
            Dt = DateTime.UtcNow,
            Key = "C",
            Pc = 88.5,
            Level = 2
        });

        var entry = await StatisticsCacheService.ComputeAsync(
            StatisticsDatabaseKind.Session, _noteDatabase, _sessionDatabase);

        _cache.Store(entry);

        Assert.Equal(StatisticsCacheService.CacheVersion, entry.CacheVersion);
        Assert.NotNull(entry.SessionData);
        Assert.Single(entry.SessionData!.Stats);
        Assert.Equal(88.5, entry.SessionData.Stats[0].Pc);
        Assert.True(_cache.TryGet(StatisticsDatabaseKind.Session, out var cached));
        Assert.NotNull(cached);
    }

    [Fact]
    public async Task ReturnVisit_UsesValidCache()
    {
        await _sessionDatabase.InitializeAsync();
        await _sessionDatabase.InsertAsync(new SessionStat { Dt = DateTime.UtcNow, Key = "G", Pc = 70 });

        var entry = await StatisticsCacheService.ComputeAsync(
            StatisticsDatabaseKind.Session, _noteDatabase, _sessionDatabase);
        _cache.Store(entry);

        var fingerprint = await StatisticsCacheService.GetFingerprintAsync(
            StatisticsDatabaseKind.Session, _noteDatabase, _sessionDatabase);

        Assert.True(_cache.TryGet(StatisticsDatabaseKind.Session, out var cached));
        Assert.NotNull(cached);
        Assert.True(StatisticsCacheService.IsValid(cached!, fingerprint));
    }

    [Fact]
    public async Task NewSession_InvalidatesCacheValidity()
    {
        await _sessionDatabase.InitializeAsync();
        await _sessionDatabase.InsertAsync(new SessionStat { Dt = DateTime.UtcNow, Key = "C", Pc = 50 });

        var entry = await StatisticsCacheService.ComputeAsync(
            StatisticsDatabaseKind.Session, _noteDatabase, _sessionDatabase);
        _cache.Store(entry);

        await _sessionDatabase.InsertAsync(new SessionStat { Dt = DateTime.UtcNow, Key = "D", Pc = 60 });

        var fingerprint = await StatisticsCacheService.GetFingerprintAsync(
            StatisticsDatabaseKind.Session, _noteDatabase, _sessionDatabase);

        Assert.True(_cache.TryGet(StatisticsDatabaseKind.Session, out var cached));
        Assert.NotNull(cached);
        Assert.False(StatisticsCacheService.IsValid(cached!, fingerprint));
    }

    [Fact]
    public async Task DeleteSession_InvalidatesCacheValidity()
    {
        await _sessionDatabase.InitializeAsync();
        var id = await _sessionDatabase.InsertAsync(new SessionStat { Dt = DateTime.UtcNow, Key = "C", Pc = 50 });
        await _sessionDatabase.InsertAsync(new SessionStat { Dt = DateTime.UtcNow, Key = "D", Pc = 60 });

        var entry = await StatisticsCacheService.ComputeAsync(
            StatisticsDatabaseKind.Session, _noteDatabase, _sessionDatabase);
        _cache.Store(entry);

        await _sessionDatabase.DeleteByIdAsync(id);
        _cache.InvalidateSessionStats();

        Assert.False(_cache.TryGet(StatisticsDatabaseKind.Session, out _));

        var fresh = await StatisticsCacheService.ComputeAsync(
            StatisticsDatabaseKind.Session, _noteDatabase, _sessionDatabase);
        Assert.NotNull(fresh.SessionData);
        Assert.Single(fresh.SessionData!.Stats);
    }

    [Fact]
    public async Task ManualRefresh_RecomputesAfterInvalidate()
    {
        await _noteDatabase.InitializeAsync();
        await _noteDatabase.InsertAsync(new NoteStat
        {
            WrittenName = "C4",
            Correct = 8,
            Wrong = 2,
            OverallCorrectCount = 8,
            OverallWrongCount = 2
        });

        var entry = await StatisticsCacheService.ComputeAsync(
            StatisticsDatabaseKind.Note, _noteDatabase, _sessionDatabase);
        _cache.Store(entry);

        await _noteDatabase.InsertAsync(new NoteStat
        {
            WrittenName = "D4",
            Correct = 5,
            Wrong = 5,
            OverallCorrectCount = 5,
            OverallWrongCount = 5
        });

        _cache.InvalidateNoteStats();
        var fresh = await StatisticsCacheService.ComputeAsync(
            StatisticsDatabaseKind.Note, _noteDatabase, _sessionDatabase);

        Assert.NotNull(fresh.NoteData);
        Assert.Equal(2, fresh.NoteData!.Stats.Count);
    }

    [Fact]
    public async Task ClearAll_InvalidatesSessionCacheAndEmptiesStats()
    {
        await _sessionDatabase.InitializeAsync();
        await _sessionDatabase.InsertAsync(new SessionStat { Dt = DateTime.UtcNow, Key = "C", Pc = 50 });
        await _sessionDatabase.InsertAsync(new SessionStat { Dt = DateTime.UtcNow, Key = "D", Pc = 60 });

        var entry = await StatisticsCacheService.ComputeAsync(
            StatisticsDatabaseKind.Session, _noteDatabase, _sessionDatabase);
        _cache.Store(entry);

        Assert.Equal(2, entry.SessionData!.Stats.Count);

        await _sessionDatabase.ClearAllAsync();
        _cache.InvalidateSessionStats();

        var fingerprint = await StatisticsCacheService.GetFingerprintAsync(
            StatisticsDatabaseKind.Session, _noteDatabase, _sessionDatabase);

        Assert.Equal(0, fingerprint.RowCount);
        Assert.Equal(0, fingerprint.Revision);
        Assert.False(_cache.TryGet(StatisticsDatabaseKind.Session, out _));
        Assert.False(StatisticsCacheService.IsValid(entry, fingerprint));

        var fresh = await StatisticsCacheService.ComputeAsync(
            StatisticsDatabaseKind.Session, _noteDatabase, _sessionDatabase);

        Assert.NotNull(fresh.SessionData);
        Assert.Empty(fresh.SessionData!.Stats);
        Assert.True(fresh.Fingerprint.Matches(fingerprint));
    }

    [Fact]
    public async Task CachedStats_MatchFreshCalculation()
    {
        await _noteDatabase.InitializeAsync();
        await _noteDatabase.InsertAsync(new NoteStat
        {
            WrittenName = "E4",
            Correct = 3,
            Wrong = 7,
            OverallCorrectCount = 3,
            OverallWrongCount = 7
        });
        await _noteDatabase.InsertAsync(new NoteStat
        {
            WrittenName = "A4",
            Correct = 9,
            Wrong = 1,
            OverallCorrectCount = 9,
            OverallWrongCount = 1
        });

        var cached = await StatisticsCacheService.ComputeAsync(
            StatisticsDatabaseKind.Note, _noteDatabase, _sessionDatabase);
        var fresh = await StatisticsDataLoader.LoadNoteStatsAsync(_noteDatabase);

        Assert.Equal(fresh.Stats.Count, cached.NoteData!.Stats.Count);
        Assert.Equal(
            fresh.Stats.Select(s => s.WrittenName).OrderBy(n => n),
            cached.NoteData.Stats.Select(s => s.WrittenName).OrderBy(n => n));
    }
}

using Microsoft.Extensions.DependencyInjection;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class NoteMasteryServiceTests : IDisposable
{
    static NoteMasteryServiceTests()
    {
        SQLitePCL.Batteries.Init();
    }

    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly string _noteDbPath;
    private readonly string _attemptDbPath;
    private readonly NoteDatabase _noteDb;
    private readonly NoteAttemptDatabase _attemptDb;
    private readonly NoteSessionService _session;
    private readonly NoteMasteryService _mastery;

    public NoteMasteryServiceTests()
    {
        SessionPreferences.TestStore = _sessionStore;
        _sessionStore.Clear();

        _noteDbPath = Path.Combine(Path.GetTempPath(), $"mm_nm_{Guid.NewGuid():N}.db3");
        _attemptDbPath = Path.Combine(Path.GetTempPath(), $"mm_na_{Guid.NewGuid():N}.db3");
        _noteDb = new NoteDatabase(_noteDbPath);
        _attemptDb = new NoteAttemptDatabase(_attemptDbPath);
        _session = new NoteSessionService();
        _session.Instrument = "concert-pitch";
        _session.Key = "C";
        // Apply after instrument auto-range so the mastery catalog uses a small fixed span.
        _session.LowestNote = "C4";
        _session.HighestNote = "E4";

        var services = new ServiceCollection();
        services.AddSingleton(_noteDb);
        services.AddSingleton(_attemptDb);
        services.AddSingleton(_session);
        services.AddSingleton<StatisticsCacheService>();
        services.AddSingleton<NoteMasteryService>();
        ServiceHelper.Initialize(services.BuildServiceProvider());

        _mastery = ServiceHelper.GetService<NoteMasteryService>()!;
    }

    private void EnsureTestRange()
    {
        _session.LowestNote = "C4";
        _session.HighestNote = "E4";
        _session.Key = "C";
        _mastery.Invalidate();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
        ServiceHelper.Initialize(new ServiceCollection().BuildServiceProvider());
        try { if (File.Exists(_noteDbPath)) File.Delete(_noteDbPath); } catch { }
        try { if (File.Exists(_attemptDbPath)) File.Delete(_attemptDbPath); } catch { }
    }

    [Fact]
    public void BuildWrittenNoteRange_IncludesAllChromaticNotes_LowToHigh()
    {
        var range = NoteMasteryService.BuildWrittenNoteRange("C4", "E4", "C", "Major");
        Assert.Equal(5, range.Count); // C C# D D# E
        Assert.Equal("C4", range[0].WrittenName);
        Assert.Equal("E4", range[^1].WrittenName);
        Assert.True(range.Select(r => r.Midi).SequenceEqual(range.Select(r => r.Midi).OrderBy(m => m)));
        Assert.Equal(range.Count, range.Select(r => r.Midi).Distinct().Count());
    }

    [Fact]
    public void BuildWrittenNoteRange_UsesFlatsInFlatKeys()
    {
        var range = NoteMasteryService.BuildWrittenNoteRange("Bb3", "C4", "Bb", "Major");
        Assert.Contains(range, r => r.WrittenName == "Bb3");
        Assert.DoesNotContain(range, r => r.WrittenName.Contains('#'));
    }

    [Fact]
    public void BuildWrittenNoteRange_FSharpMajor_UsesESharpNotF()
    {
        var range = NoteMasteryService.BuildWrittenNoteRange("E4", "F#4", "F#", "Major");
        Assert.Contains(range, r => r.WrittenName == "E#4");
        Assert.DoesNotContain(range, r => r.WrittenName == "F4");
        Assert.Equal(range.Count, range.Select(r => r.Midi).Distinct().Count());
    }

    [Fact]
    public void BuildWrittenNoteRange_CbMajor_UsesCbAndFb_NotBAndE()
    {
        var range = NoteMasteryService.BuildWrittenNoteRange("Cb4", "Gb4", "Cb", "Major");
        Assert.Contains(range, r => r.WrittenName == "Cb4");
        Assert.Contains(range, r => r.WrittenName == "Fb4");
        Assert.DoesNotContain(range, r => r.WrittenName is "B3" or "E4");
        Assert.Equal(range.Count, range.Select(r => r.Midi).Distinct().Count());
        Assert.DoesNotContain(
            range.Select(r => r.WrittenName),
            name => name.StartsWith("C#", StringComparison.Ordinal)
                   || name.StartsWith("F#", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildWrittenNoteRange_MatchesSpellWrittenPitch_ForSelectedKey()
    {
        const string key = "Db";
        const string scale = "Major";
        var range = NoteMasteryService.BuildWrittenNoteRange("C4", "C5", key, scale);
        foreach (var (name, midi) in range)
            Assert.Equal(NoteSessionService.SpellWrittenPitch(midi, key, scale), name);
    }

    [Fact]
    public void FindStatForDisplay_PrefersExactName_ThenRichestEnharmonic()
    {
        var byName = new Dictionary<string, NoteStat>(StringComparer.OrdinalIgnoreCase)
        {
            ["F#4"] = new NoteStat
            {
                WrittenName = "F#4",
                OverallCorrectCount = 2,
                OverallWrongCount = 0,
                Correct = 2,
                Wrong = 0,
                Streak = 1,
            },
            ["Gb4"] = new NoteStat
            {
                WrittenName = "Gb4",
                OverallCorrectCount = 8,
                OverallWrongCount = 1,
                Correct = 8,
                Wrong = 1,
                Streak = 4,
            },
        };

        int midi = NoteSessionService.NoteNameToMidi("Gb4");
        var exact = NoteMasteryService.FindStatForDisplay(byName, "Gb4", midi);
        Assert.Equal("Gb4", exact!.WrittenName);

        var bySharpOnly = new Dictionary<string, NoteStat>(StringComparer.OrdinalIgnoreCase)
        {
            ["F#4"] = byName["F#4"],
        };
        var enharmonic = NoteMasteryService.FindStatForDisplay(bySharpOnly, "Gb4", midi);
        Assert.Equal("F#4", enharmonic!.WrittenName);

        var richest = NoteMasteryService.FindStatForDisplay(byName, "F#4", midi);
        Assert.Equal("F#4", richest!.WrittenName); // exact wins over richer Gb4
    }

    [Fact]
    public async Task Snapshot_CountsAndPercents_MatchClassifier()
    {
        await _noteDb.InitializeAsync();
        await _noteDb.InsertOrReplaceAsync(new NoteStat
        {
            WrittenName = "C4",
            Streak = 5,
            OverallCorrectCount = 10,
            OverallWrongCount = 0,
            PitchCorrectCount = 10,
            PitchWrongCount = 0,
            Correct = 10,
            Wrong = 0,
        });
        await _noteDb.InsertOrReplaceAsync(new NoteStat
        {
            WrittenName = "D4",
            OverallCorrectCount = 1,
            OverallWrongCount = 9,
            PitchCorrectCount = 1,
            PitchWrongCount = 9,
            Correct = 1,
            Wrong = 9,
        });

        _session.MasteredMethod = "Streak";
        _session.StreakCrit = 3;
        EnsureTestRange();

        var snap = await _mastery.GetSnapshotAsync(forceRefresh: true);
        Assert.Equal(5, snap.TotalCount);
        Assert.Equal(1, snap.MasteredCount);
        Assert.Equal(1, snap.NeedsPracticeCount);
        Assert.Equal(3, snap.NotYetAttemptedCount);
        Assert.Equal(20, snap.MasteredPercent, 1);
        Assert.Contains("1 of 5 notes mastered", snap.SummaryText);
    }

    [Fact]
    public async Task Cache_InvalidatesAfterNewAttemptsRecorded()
    {
        await _noteDb.InitializeAsync();
        EnsureTestRange();
        var first = await _mastery.GetSnapshotAsync(forceRefresh: true);
        Assert.Equal(5, first.TotalCount);

        await _noteDb.InsertOrReplaceAsync(new NoteStat
        {
            WrittenName = "E4",
            Streak = 5,
            OverallCorrectCount = 8,
            OverallWrongCount = 0,
            PitchCorrectCount = 8,
            PitchWrongCount = 0,
            Correct = 8,
            Wrong = 0,
        });

        // Without invalidate, fingerprint change should still rebuild on next Get
        var second = await _mastery.GetSnapshotAsync(forceRefresh: false);
        Assert.Equal(1, second.MasteredCount);

        _mastery.Invalidate();
        var third = await _mastery.GetSnapshotAsync(forceRefresh: false);
        Assert.Equal(1, third.MasteredCount);
    }

    [Fact]
    public async Task InstrumentScopedAttempts_DoNotMergeAcrossInstruments()
    {
        await _attemptDb.InitializeAsync();
        SessionPreferences.Set("MaxAttemptsPerNote", 100);
        await _attemptDb.SaveAttemptAsync(new NoteAttempt
        {
            DateTime = DateTime.UtcNow,
            WrittenNoteName = "C4",
            Instrument = "bb-clarinet",
            MidiNumber = 60,
            PitchCorrect = true,
        });

        _session.Instrument = "concert-pitch";
        EnsureTestRange();
        var snap = await _mastery.GetSnapshotAsync(forceRefresh: true);
        var c4 = snap.Notes.First(n => n.WrittenNoteName == "C4");
        Assert.Null(c4.LastPracticedUtc);

        _session.Instrument = "bb-clarinet";
        EnsureTestRange();
        var snapBb = await _mastery.GetSnapshotAsync(forceRefresh: true);
        var c4Bb = snapBb.Notes.First(n => n.WrittenNoteName == "C4");
        Assert.NotNull(c4Bb.LastPracticedUtc);
    }
}

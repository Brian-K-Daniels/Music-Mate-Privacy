using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SQLite;
using musicmate.Utilities;

namespace musicmate.Services;

public class SessionStat
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string Key { get; set; } = "";
    
    public string Tune { get; set; } = "";
    public string Instrument { get; set; } = "";  // NEW: Instrument column
    public DateTime Dt { get; set; }      // Date and time
    public string Sc { get; set; } = "";  // Scale name
    public string Hi { get; set; } = "";  // Highest note
    public string Lo { get; set; } = "";  // Lowest note
    public double Pc { get; set; }        // % correct
    public double PcRaw { get; set; }     // % correct raw (before any adjustments)
    public double Tp { get; set; }        // Tempo (mean BPM)
    public double Ts { get; set; }        // Tempo StdDev     
    // Coefficient of variation as percentage (100 * Ts / Tp). Computed on demand, not stored in DB.
    [Ignore]
    public double Cf
    {
        get
        {
            try
            {
                if (Tp == 0) return 0.0;
                return 100.0 * Ts / Tp;
            }
            catch
            {
                return 0.0;
            }
        }
    }
    [Ignore]
    public bool IsSelected { get; set; }
    [Ignore]
    public string DisplayKey => Sc == "Random" ? "C" : Key;
    public string KeyAndScale => $"{Key} {Sc}";
    [Ignore]
    public Microsoft.Maui.Graphics.Color ContrastingTextColor { get; set; }= Microsoft.Maui.Graphics.Colors.Red;
}

public class SessionDatabase
{
    private readonly SQLiteAsyncConnection _db;
    private readonly string _dbPath;

    public SessionDatabase(string dbPath)
    {
        _dbPath = dbPath;
        _db = new SQLiteAsyncConnection(dbPath);
    }

    // Add this public property
    public string DatabasePath => _dbPath;

    public async Task InitializeAsync()
    {
        await _db.CreateTableAsync<SessionStat>();
        await EnsureTuneColumnAsync();
        await EnsureInstrumentColumnAsync();  // NEW: Ensure Instrument column exists
    }

    private async Task EnsureTuneColumnAsync()
    {
        var columns = await _db.GetTableInfoAsync(nameof(SessionStat));
        if (columns.All(c => !string.Equals(c.Name, nameof(SessionStat.Tune), StringComparison.OrdinalIgnoreCase)))
        {
            await _db.ExecuteAsync($"ALTER TABLE {nameof(SessionStat)} ADD COLUMN {nameof(SessionStat.Tune)} TEXT NOT NULL DEFAULT ''");
        }
    }

    private async Task EnsureInstrumentColumnAsync()  // NEW: Migration method
    {
        var columns = await _db.GetTableInfoAsync(nameof(SessionStat));
        if (columns.All(c => !string.Equals(c.Name, nameof(SessionStat.Instrument), StringComparison.OrdinalIgnoreCase)))
        {
            await _db.ExecuteAsync($"ALTER TABLE {nameof(SessionStat)} ADD COLUMN {nameof(SessionStat.Instrument)} TEXT NOT NULL DEFAULT ''");
        }
    }

    public async Task<int> InsertAsync(SessionStat stat)
    {
        try
        {
            Utils.Log($"[SessionDatabase] Inserting: {System.Text.Json.JsonSerializer.Serialize(stat)}");
            return await _db.InsertAsync(stat);
        }
        catch (Exception ex)
        {
            Utils.Log($"[SessionDatabase] InsertAsync exception: {ex}");
            throw;
        }
    }

    public async Task<List<SessionStat>> GetAllAsync()
    {
        Utils.Log("[SessionDatabase] GetAllAsync called");
        var result = await _db.Table<SessionStat>().OrderByDescending(s => s.Dt).ToListAsync();
        Utils.Log($"[SessionDatabase] Returning {result.Count} session records");
        return result;
    }

    public Task<int> DeleteByIdAsync(int id)
    {
        return _db.Table<SessionStat>().DeleteAsync(s => s.Id == id);
    }

    public Task<int> ClearAllAsync()
    {
        return _db.DeleteAllAsync<SessionStat>();
    }

    public async Task DeleteDatabaseAsync()
    {
#if DEBUG
        await _db.CloseAsync();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
#else
        throw new InvalidOperationException("DeleteDatabaseAsync is only available in DEBUG builds.");
#endif
    }

        /// <summary>
        /// Deletes the oldest SessionStat rows by date until the DB file is under
        /// <paramref name="maxBytes"/>. Does nothing if already within limit.
        /// </summary>
        public async Task PruneToSizeLimitAsync(long maxBytes)
        {
            try
            {
                var fileInfo = new FileInfo(_dbPath);
                if (!fileInfo.Exists || fileInfo.Length <= maxBytes)
                    return;

                while (true)
                {
                    fileInfo.Refresh();
                    if (fileInfo.Length <= maxBytes) break;

                    var oldest = await _db.Table<SessionStat>()
                        .OrderBy(s => s.Dt)
                        .FirstOrDefaultAsync();

                    if (oldest == null) break;

                    await _db.DeleteAsync(oldest);
                }
            }
            catch (Exception ex)
            {
                Utils.Log($"[SessionDatabase.PruneToSizeLimitAsync] {ex.Message}");
            }
        }
}

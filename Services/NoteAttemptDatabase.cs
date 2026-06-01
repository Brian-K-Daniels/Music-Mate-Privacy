#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using musicmate.Models;
using musicmate.Utilities;
using SQLite;

namespace musicmate.Services
{
    /// <summary>
    /// Persists individual <see cref="NoteAttempt"/> rows and enforces the rolling
    /// per-note retention limit (MaxAttemptsPerNote).
    ///
    /// Rolling limit:
    ///   After saving a new attempt, the oldest attempts for the same note+instrument
    ///   combination are deleted so the total count does not exceed MaxAttemptsPerNote.
    ///
    /// MaxAttemptsPerNote:
    ///   Read from <c>Preferences.Default.Get("MaxAttemptsPerNote", 100)</c> so it
    ///   is persisted by the standard app preferences mechanism.  Default = 100.
    ///
    /// Note identity key:
    ///   WrittenNoteName + Instrument, e.g. "C4" + "Bb Clarinet, Bb, ..."
    ///   This matches what is already used in NoteStat, ensuring consistency.
    /// </summary>
    public class NoteAttemptDatabase
    {
        private readonly SQLiteAsyncConnection _db;
        private readonly string _dbPath;

        public string DatabasePath => _dbPath;

        public NoteAttemptDatabase(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("Database path must be provided.", nameof(dbPath));
            _dbPath = dbPath;
            _db = new SQLiteAsyncConnection(dbPath);
        }

        public async Task InitializeAsync()
        {
            await _db.CreateTableAsync<NoteAttempt>();
        }

        /// <summary>
        /// Saves one attempt and then prunes old attempts for the same note+instrument
        /// combination if the count exceeds MaxAttemptsPerNote.
        ///
        /// Pruning happens here, immediately after insert, so the table never grows
        /// beyond (MaxAttemptsPerNote + a small transient margin).
        /// </summary>
        public async Task SaveAttemptAsync(NoteAttempt attempt)
        {
            await _db.InsertAsync(attempt);

            // Rolling limit: keep only the most recent N attempts per note+instrument.
            int maxKeep = Microsoft.Maui.Storage.Preferences.Default.Get("MaxAttemptsPerNote", 100);
            await PruneAttemptsForNoteAsync(attempt.WrittenNoteName, attempt.Instrument, maxKeep);
        }

        /// <summary>
        /// Discards the oldest attempts for one note+instrument pair so at most
        /// <paramref name="maxKeep"/> rows remain.
        /// </summary>
        private async Task PruneAttemptsForNoteAsync(string writtenNoteName, string instrument, int maxKeep)
        {
            try
            {
                // Count rows for this note+instrument (identity key for rolling limit).
                int count = await _db.Table<NoteAttempt>()
                    .Where(a => a.WrittenNoteName == writtenNoteName && a.Instrument == instrument)
                    .CountAsync();

                int excess = count - maxKeep;
                if (excess <= 0) return;

                // Find the oldest excess rows (lowest AttemptId = earliest insert order).
                var toDelete = await _db.Table<NoteAttempt>()
                    .Where(a => a.WrittenNoteName == writtenNoteName && a.Instrument == instrument)
                    .OrderBy(a => a.AttemptId)
                    .Take(excess)
                    .ToListAsync();

                foreach (var row in toDelete)
                    await _db.DeleteAsync(row);
            }
            catch (Exception ex)
            {
                Utils.Log($"[NoteAttemptDatabase.PruneAttemptsForNoteAsync] {ex.Message}");
            }
        }

        public Task<List<NoteAttempt>> GetAllAsync()
            => _db.Table<NoteAttempt>().ToListAsync();

        public Task<List<NoteAttempt>> GetByNoteAsync(string writtenNoteName, string instrument)
            => _db.Table<NoteAttempt>()
                  .Where(a => a.WrittenNoteName == writtenNoteName && a.Instrument == instrument)
                  .OrderByDescending(a => a.AttemptId)
                  .ToListAsync();

        /// <summary>
        /// Returns attempts for one note+instrument pair that pass the outlier filter
        /// (<see cref="NoteAttempt.IsReliableForStats"/> == true).
        ///
        /// Raw rows are never deleted — the filter is applied in memory after the query
        /// because SQLite-net cannot translate the computed <c>[Ignore]</c> property.
        /// </summary>
        public async Task<List<NoteAttempt>> GetReliableForStatsAsync(
            string writtenNoteName, string instrument)
        {
            var all = await GetByNoteAsync(writtenNoteName, instrument);
            return all.FindAll(a => a.IsReliableForStats);
        }

        public Task<int> GetCountForNoteAsync(string writtenNoteName, string instrument)
            => _db.Table<NoteAttempt>()
                  .Where(a => a.WrittenNoteName == writtenNoteName && a.Instrument == instrument)
                  .CountAsync();

        public Task<int> ClearAllAsync()
            => _db.DeleteAllAsync<NoteAttempt>();

        /// <summary>
        /// Deletes the oldest rows until the DB file is under <paramref name="maxBytes"/>.
        /// Uses the same strategy as NoteDatabase.PruneToSizeLimitAsync.
        /// </summary>
        public async Task PruneToSizeLimitAsync(long maxBytes)
        {
            try
            {
                var info = new FileInfo(_dbPath);
                if (!info.Exists || info.Length <= maxBytes) return;

                while (true)
                {
                    info.Refresh();
                    if (info.Length <= maxBytes) break;
                    var oldest = await _db.Table<NoteAttempt>()
                        .OrderBy(a => a.AttemptId)
                        .FirstOrDefaultAsync();
                    if (oldest == null) break;
                    await _db.DeleteAsync(oldest);
                }
            }
            catch (Exception ex)
            {
                Utils.Log($"[NoteAttemptDatabase.PruneToSizeLimitAsync] {ex.Message}");
            }
        }
    }
}

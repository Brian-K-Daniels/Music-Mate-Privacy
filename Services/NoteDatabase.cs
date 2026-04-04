#nullable enable
using System;
using System.IO;
using SQLite;
using System.Collections.Generic;
using System.Threading.Tasks;
using musicmate.Utilities;

namespace musicmate.Services
{
    public class NoteDatabase
    {
        private readonly SQLiteAsyncConnection _db;
        private readonly string _dbPath;

        public NoteDatabase(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("Database path must be provided.", nameof(dbPath));

            _dbPath = dbPath;
            _db = new SQLiteAsyncConnection(dbPath);
        }

        public async Task InitializeAsync()
        {
            if (DeviceInfo.Platform == DevicePlatform.Android &&
                DeviceInfo.Manufacturer?.ToLowerInvariant().Contains("google") == true &&
                DeviceInfo.Model?.ToLowerInvariant().Contains("pixel") == true)
            {
                Utils.Log("NoteDatabase initialized on Google Pixel device.");
            }
            await _db.CreateTableAsync<NoteStat>();
        }

        public Task<List<NoteStat>> GetAllAsync()
        {
            return _db.Table<NoteStat>().ToListAsync();
        }

        public Task<NoteStat> GetByWrittenNameAsync(string writtenName)
        {
            return _db.Table<NoteStat>().FirstOrDefaultAsync(n => n.WrittenName == writtenName);
        }

        public Task<int> InsertAsync(NoteStat stat)
        {
            return _db.InsertAsync(stat);
        }

        public Task<int> InsertOrReplaceAsync(NoteStat stat)
        {
            return _db.InsertOrReplaceAsync(stat);
        }

        public Task<int> UpdateAsync(NoteStat stat)
        {
            return _db.UpdateAsync(stat);
        }

        // Clear = remove all rows
        public Task<int> ClearAllAsync()
        {
            return _db.DeleteAllAsync<NoteStat>();
        }

        // Delete = remove the DB file so it will be recreated on next run (DEBUG only)
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
    }
}

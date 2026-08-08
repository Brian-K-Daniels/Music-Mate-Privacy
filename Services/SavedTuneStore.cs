#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Persists user-authored <see cref="PracticeTune"/> instances as JSON under AppData.
    /// Titles that start with <see cref="TitlePrefix"/> participate in automatic numbering
    /// ("Saved Tune 1", "Saved Tune 2", …) with lowest-available reuse after delete.
    /// </summary>
    public sealed class SavedTuneStore
    {
        public const string TitlePrefix = "Saved Tune";
        public const string FileName = "saved_tunes.json";

        private static readonly Regex NumberedTitleRegex = new(
            @"^Saved Tune\s+(\d+)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly string _path;
        private readonly object _gate = new();
        private List<SavedTuneDto>? _cache;

        public SavedTuneStore(string? path = null)
        {
            _path = path ?? Path.Combine(FileSystem.AppDataDirectory, FileName);
        }

        /// <summary>True when the title contains the saved-tune naming marker.</summary>
        public static bool IsSavedTuneTitle(string? title)
            => !string.IsNullOrWhiteSpace(title)
               && title.Contains(TitlePrefix, StringComparison.Ordinal);

        /// <summary>True when <paramref name="title"/> is a built-in library title (never deletable here).</summary>
        public static bool IsBuiltInTuneTitle(string? title)
            => !string.IsNullOrEmpty(title)
               && TuneLibrary.All.Any(t => string.Equals(t.Title, title, StringComparison.Ordinal));

        public IReadOnlyList<string> Titles
        {
            get
            {
                lock (_gate)
                    return LoadUnlocked().Select(t => t.Title).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public PracticeTune? GetByTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            lock (_gate)
            {
                var dto = LoadUnlocked().FirstOrDefault(t =>
                    string.Equals(t.Title, title, StringComparison.Ordinal));
                return dto == null ? null : ToPracticeTune(dto);
            }
        }

        /// <summary>Lowest positive integer not used by an existing "Saved Tune n" title.</summary>
        public int GetLowestAvailableNumber()
        {
            lock (_gate)
            {
                var used = new HashSet<int>();
                foreach (var dto in LoadUnlocked())
                {
                    if (TryParseNumberedTitle(dto.Title, out int n))
                        used.Add(n);
                }

                int i = 1;
                while (used.Contains(i))
                    i++;
                return i;
            }
        }

        public string SuggestNextTitle() => $"{TitlePrefix} {GetLowestAvailableNumber()}";

        public void Save(PracticeTune tune)
        {
            ArgumentNullException.ThrowIfNull(tune);
            if (string.IsNullOrWhiteSpace(tune.Title))
                throw new ArgumentException("Tune title must not be empty.", nameof(tune));
            if (IsBuiltInTuneTitle(tune.Title))
                throw new InvalidOperationException("Cannot overwrite a built-in tune.");

            var dto = FromPracticeTune(tune);
            lock (_gate)
            {
                var list = LoadUnlocked();
                int idx = list.FindIndex(t => string.Equals(t.Title, tune.Title, StringComparison.Ordinal));
                if (idx >= 0)
                    list[idx] = dto;
                else
                    list.Add(dto);
                PersistUnlocked(list);
            }
        }

        public bool Delete(string title)
        {
            if (string.IsNullOrWhiteSpace(title) || IsBuiltInTuneTitle(title))
                return false;

            lock (_gate)
            {
                var list = LoadUnlocked();
                int removed = list.RemoveAll(t => string.Equals(t.Title, title, StringComparison.Ordinal));
                if (removed == 0)
                    return false;
                PersistUnlocked(list);
                return true;
            }
        }

        /// <summary>Clears all saved tunes (Factory Reset).</summary>
        public void ClearAll()
        {
            lock (_gate)
            {
                _cache = new List<SavedTuneDto>();
                try
                {
                    if (File.Exists(_path))
                        File.Delete(_path);
                }
                catch
                {
                    PersistUnlocked(_cache);
                }
            }
        }

        public static bool TryParseNumberedTitle(string? title, out int number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(title))
                return false;
            var m = NumberedTitleRegex.Match(title.Trim());
            if (!m.Success)
                return false;
            return int.TryParse(m.Groups[1].Value, out number) && number > 0;
        }

        /// <summary>Deep-copies a practice tune under a new title.</summary>
        public static PracticeTune CloneWithTitle(PracticeTune source, string title)
        {
            ArgumentNullException.ThrowIfNull(source);
            var clone = new PracticeTune(title, source.TimeSignature, source.Key);
            foreach (var measure in source.Measures)
            {
                var m = clone.AppendMeasure();
                foreach (var note in measure.Notes)
                {
                    m.AddNote(note.IsRest
                        ? MusicNote.Rest(note.Duration)
                        : new MusicNote(note.MidiNumber, note.SpelledName, note.Duration));
                }
            }
            return clone;
        }

        /// <summary>Builds a <see cref="PracticeTune"/> from staff/session generated notes.</summary>
        public static PracticeTune FromGeneratedNotes(
            string title,
            IReadOnlyList<GeneratedNote> notes,
            TimeSignature timeSignature,
            string? key)
        {
            ArgumentNullException.ThrowIfNull(notes);
            if (notes.Count == 0)
                throw new ArgumentException("Cannot save an empty tune.", nameof(notes));

            var ordered = notes
                .OrderBy(n => n.BeatPosition ?? 0.0)
                .ThenBy(n => n.MeasureIndex ?? 0)
                .ToList();

            var tune = new PracticeTune(title, timeSignature, key);
            bool hasMeasureIndex = ordered.Any(n => n.MeasureIndex.HasValue);

            if (hasMeasureIndex)
            {
                foreach (var group in ordered.GroupBy(n => n.MeasureIndex ?? 0).OrderBy(g => g.Key))
                {
                    var measure = tune.AppendMeasure();
                    foreach (var gn in group)
                        measure.AddNote(ToMusicNote(gn));
                }
            }
            else
            {
                double beatsPerMeasure = timeSignature.TotalBeats;
                double cursor = 0.0;
                Measure? measure = null;
                double measureBeats = 0.0;

                foreach (var gn in ordered)
                {
                    double dur = gn.Duration.ToBeatValue();
                    if (measure == null || measureBeats + dur > beatsPerMeasure + 1e-9)
                    {
                        measure = tune.AppendMeasure();
                        measureBeats = 0.0;
                    }

                    measure.AddNote(ToMusicNote(gn));
                    measureBeats += dur;
                    cursor += dur;
                    _ = cursor;
                }
            }

            return tune;
        }

        private static MusicNote ToMusicNote(GeneratedNote gn)
            => gn.IsRest
                ? MusicNote.Rest(gn.Duration)
                : new MusicNote(
                    gn.MidiNumber,
                    string.IsNullOrWhiteSpace(gn.SpelledName) ? "C4" : gn.SpelledName,
                    gn.Duration);

        private List<SavedTuneDto> LoadUnlocked()
        {
            if (_cache != null)
                return _cache;

            try
            {
                if (!File.Exists(_path))
                {
                    _cache = new List<SavedTuneDto>();
                    return _cache;
                }

                string json = File.ReadAllText(_path);
                _cache = JsonSerializer.Deserialize<List<SavedTuneDto>>(json, JsonOptions)
                         ?? new List<SavedTuneDto>();
            }
            catch
            {
                _cache = new List<SavedTuneDto>();
            }

            return _cache;
        }

        private void PersistUnlocked(List<SavedTuneDto> list)
        {
            _cache = list;
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(list, JsonOptions));
        }

        private static SavedTuneDto FromPracticeTune(PracticeTune tune)
            => new()
            {
                Title = tune.Title,
                TimeSignature = tune.TimeSignature.ToString(),
                Key = tune.Key,
                Measures = tune.Measures.Select(m => new SavedMeasureDto
                {
                    Notes = m.Notes.Select(n => new SavedNoteDto
                    {
                        MidiNumber = n.MidiNumber,
                        SpelledName = n.SpelledName,
                        Duration = n.Duration.ToString(),
                        IsRest = n.IsRest,
                    }).ToList(),
                }).ToList(),
            };

        private static PracticeTune ToPracticeTune(SavedTuneDto dto)
        {
            var ts = TimeSignature.FromDisplayString(dto.TimeSignature);
            var tune = new PracticeTune(dto.Title, ts, dto.Key);
            foreach (var measureDto in dto.Measures ?? Enumerable.Empty<SavedMeasureDto>())
            {
                var measure = tune.AppendMeasure();
                foreach (var noteDto in measureDto.Notes ?? Enumerable.Empty<SavedNoteDto>())
                {
                    var duration = Enum.TryParse<NoteDuration>(noteDto.Duration, ignoreCase: true, out var d)
                        ? d
                        : NoteDuration.Quarter;
                    measure.AddNote(noteDto.IsRest
                        ? MusicNote.Rest(duration)
                        : new MusicNote(noteDto.MidiNumber, noteDto.SpelledName, duration));
                }
            }
            return tune;
        }

        private sealed class SavedTuneDto
        {
            public string Title { get; set; } = string.Empty;
            public string TimeSignature { get; set; } = "4/4";
            public string? Key { get; set; }
            public List<SavedMeasureDto> Measures { get; set; } = new();
        }

        private sealed class SavedMeasureDto
        {
            public List<SavedNoteDto> Notes { get; set; } = new();
        }

        private sealed class SavedNoteDto
        {
            public int MidiNumber { get; set; }
            public string SpelledName { get; set; } = string.Empty;
            public string Duration { get; set; } = nameof(NoteDuration.Quarter);
            public bool IsRest { get; set; }
        }
    }
}

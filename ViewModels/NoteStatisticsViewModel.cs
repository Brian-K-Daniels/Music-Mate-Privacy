using musicmate.Services;
using musicmate.Diagnostics;
using musicmate.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using musicmate.Utilities;
using System.Threading;
using System.Windows.Input;

namespace musicmate.ViewModels
{
    public class NoteStatisticsViewModel : INotifyPropertyChanged
    {
        private readonly NoteDatabase _noteDatabase;
        private readonly SessionDatabase _sessionDatabase;
        private readonly ThemeService _themeService;
        private readonly NoteSessionService _session;
        private readonly StatisticsCacheService _statisticsCache;
        private readonly NoteMasteryService _noteMasteryService;
        private int _loadGeneration;
        public Color PanelBackgroundColor => _themeService.PanelBackgroundColor;
        public Color ContrastingTextColor => _themeService.ContrastingTextColor;
        public bool IsNoteDatabase => SelectedDatabase == "Note";
        public bool IsSessionDatabase => SelectedDatabase == "Session";
        public bool IsChildResultsDatabase => SelectedDatabase == "Child Results";
        public bool IsMasteryDatabase => SelectedDatabase == "Mastery";
        public ObservableCollection<string> DatabaseOptions { get; } =
            new() { "Note", "Mastery", "Session", "Child Results" };
        // Remove color properties from here; use ThemeService for colors in the view.
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<NoteStat> NoteStats { get; } = new();
        public ObservableCollection<SessionStat> SessionStats { get; } = new();
        public ObservableCollection<NoteMasteryItemViewModel> MasteryNotes { get; } = new();

        // Sorting state for Note Stats
        private string _noteCurrentSortColumn = "";
        private bool _noteIsAscending = true;

        // Sorting state for Session Stats
        private string _sessionCurrentSortColumn = "";
        private bool _sessionIsAscending = true;

        // Commands
        public ICommand SortNoteStatsCommand { get; }
        public ICommand SortSessionStatsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SelectMasteryNoteCommand { get; }
        public ICommand PracticeThisNoteCommand { get; }
        public ICommand EmphasizeNoteCommand { get; }
        public ICommand CloseMasteryDetailCommand { get; }

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                }
            }
        }

        private DateTime? _lastUpdatedUtc;
        public DateTime? LastUpdatedUtc
        {
            get => _lastUpdatedUtc;
            private set
            {
                if (_lastUpdatedUtc != value)
                {
                    _lastUpdatedUtc = value;
                    OnPropertyChanged(nameof(LastUpdatedUtc));
                    OnPropertyChanged(nameof(LastUpdatedText));
                    OnPropertyChanged(nameof(HasLastUpdated));
                }
            }
        }

        public bool HasLastUpdated => LastUpdatedUtc.HasValue;

        public string LastUpdatedText =>
            LastUpdatedUtc.HasValue
                ? $"Last updated {LastUpdatedUtc.Value.ToLocalTime():g}"
                : string.Empty;

        public NoteStatisticsViewModel(
            NoteDatabase noteDatabase,
            SessionDatabase sessionDatabase,
            ThemeService themeService,
            NoteSessionService session,
            StatisticsCacheService statisticsCache,
            NoteMasteryService noteMasteryService)
        {
            _noteDatabase = noteDatabase;
            _sessionDatabase = sessionDatabase;
            _themeService = themeService;
            _session = session;
            _statisticsCache = statisticsCache;
            _noteMasteryService = noteMasteryService;
            _session.PropertyChanged += OnSessionPropertyChanged;

            SortNoteStatsCommand = new Command<string>(SortNoteStatsByColumn);
            SortSessionStatsCommand = new Command<string>(SortSessionStatsByColumn);
            RefreshCommand = new Command(() => _ = LoadAsync(forceRefresh: true));
            SelectMasteryNoteCommand = new Command<NoteMasteryItemViewModel>(SelectMasteryNote);
            PracticeThisNoteCommand = new Command(async () => await NavigatePracticeAsync(practiceThisNote: true));
            EmphasizeNoteCommand = new Command(async () => await NavigatePracticeAsync(practiceThisNote: false));
            CloseMasteryDetailCommand = new Command(() => SelectedMasteryNote = null);
        }

        private NoteMasterySnapshot? _masterySnapshot;
        private NoteMasteryItemViewModel? _selectedMasteryNote;

        public string MasterySummaryText => _masterySnapshot?.SummaryText ?? string.Empty;

        public string MasteryMasteredCountText =>
            $"★ {_masterySnapshot?.MasteredCount ?? 0}";
        public string MasteryImprovingCountText =>
            $"▲ {_masterySnapshot?.ImprovingCount ?? 0}";
        public string MasteryNeedsPracticeCountText =>
            $"● {_masterySnapshot?.NeedsPracticeCount ?? 0}";
        public string MasteryNotTriedCountText =>
            $"○ {_masterySnapshot?.NotYetAttemptedCount ?? 0}";

        public bool PreferBassClef => _masterySnapshot?.PreferBassClef ?? false;

        public NoteMasteryItemViewModel? SelectedMasteryNote
        {
            get => _selectedMasteryNote;
            set
            {
                if (_selectedMasteryNote == value) return;
                if (_selectedMasteryNote is not null)
                    _selectedMasteryNote.IsSelected = false;
                _selectedMasteryNote = value;
                if (_selectedMasteryNote is not null)
                    _selectedMasteryNote.IsSelected = true;
                OnPropertyChanged(nameof(SelectedMasteryNote));
                OnPropertyChanged(nameof(HasSelectedMasteryNote));
                OnPropertyChanged(nameof(SelectedMasteryDetailText));
                OnPropertyChanged(nameof(SelectedMasteryNoteNameText));
                OnPropertyChanged(nameof(SelectedMasteryStateText));
                OnPropertyChanged(nameof(SelectedMasteryStateDescription));
            }
        }

        public bool HasSelectedMasteryNote => SelectedMasteryNote is not null;

        public string SelectedMasteryNoteNameText =>
            SelectedMasteryNote is null
                ? string.Empty
                : SelectedMasteryNote.WrittenNoteName;

        public string SelectedMasteryStateText =>
            SelectedMasteryNote is null
                ? string.Empty
                : $"{SelectedMasteryNote.StateMarker} {SelectedMasteryNote.StateDisplayName}";

        public string SelectedMasteryStateDescription =>
            SelectedMasteryNote is null
                ? string.Empty
                : NoteMasteryStateLabels.Description(SelectedMasteryNote.MasteryState);

        public string SelectedMasteryDetailText
        {
            get
            {
                var n = SelectedMasteryNote;
                if (n is null) return string.Empty;

                var lines = new List<string>
                {
                    $"Instrument: {n.InstrumentDisplayName}",
                };

                if (n.AttemptCount > 0)
                {
                    lines.Add($"Attempts: {n.AttemptCount}");
                    lines.Add($"Correct: {n.CorrectAttempts}");
                    lines.Add($"Incorrect: {n.IncorrectAttempts}");
                    if (n.PitchAccuracyPercent.HasValue)
                        lines.Add($"Pitch accuracy: {n.PitchAccuracyPercent.Value:0}%");
                    else
                        lines.Add("Pitch accuracy: Not available");
                    if (n.TimingAccuracyPercent.HasValue)
                        lines.Add($"Timing accuracy: {n.TimingAccuracyPercent.Value:0}%");
                    if (n.LongestStreak.HasValue)
                        lines.Add($"Longest streak: {n.LongestStreak.Value}");
                }
                else
                {
                    lines.Add("Attempts: Not tried yet");
                }

                lines.Add(n.LastPracticedUtc.HasValue
                    ? $"Last practiced: {n.LastPracticedUtc.Value.ToLocalTime():g}"
                    : "Last practiced: Not available");

                return string.Join(Environment.NewLine, lines);
            }
        }

        private void SelectMasteryNote(NoteMasteryItemViewModel? note)
        {
            if (note is null) return;
            SelectedMasteryNote = note;
            DebugLog.WriteLine(
                DebugLogCategory.Statistics,
                $"[NoteMastery] Selected note={note.WrittenNoteName} state={note.MasteryState}");
        }

        private async Task NavigatePracticeAsync(bool practiceThisNote)
        {
            var note = SelectedMasteryNote;
            if (note is null) return;

            if (practiceThisNote)
            {
                DebugLog.WriteLine(
                    DebugLogCategory.Statistics,
                    $"[NoteMastery] Practice This Note={note.WrittenNoteName}");
                _session.BeginPracticeThisNote(note.WrittenNoteName);
            }
            else
            {
                DebugLog.WriteLine(
                    DebugLogCategory.Statistics,
                    $"[NoteMastery] Emphasize note={note.WrittenNoteName}");
                _session.BeginEmphasizedNotePractice(
                    note.WrittenNoteName,
                    NoteMasteryPreferenceDefaults.EmphasizedNoteSelectionPercent);
            }

            await Shell.Current.GoToAsync("//MusicPage");
        }

        // Expose a debug flag to the view so debug-only UI can be shown/hidden via binding
        public bool IsDebug
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        public async Task LoadAsync(bool forceRefresh = false)
        {
            int loadGeneration = Interlocked.Increment(ref _loadGeneration);

            if (IsMasteryDatabase)
            {
                Utils.Log("[NoteStatisticsViewModel] LoadAsync Note Mastery");
                DebugLog.WriteLine(DebugLogCategory.Statistics, "[NoteMastery] Opening Note Mastery");
                IsLoading = true;
                try
                {
                    var snapshot = await _noteMasteryService.GetSnapshotAsync(forceRefresh);
                    if (loadGeneration != Volatile.Read(ref _loadGeneration))
                        return;

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (loadGeneration != Volatile.Read(ref _loadGeneration))
                            return;
                        ApplyMasterySnapshot(snapshot);
                    });
                }
                catch (Exception ex)
                {
                    Utils.Log($"[NoteStatisticsViewModel] Mastery load error: {ex}");
                }
                finally
                {
                    if (loadGeneration == Volatile.Read(ref _loadGeneration))
                        IsLoading = false;
                }

                return;
            }

            var kind = StatisticsCacheService.MapSelectedDatabase(SelectedDatabase);
            Utils.Log($"[NoteStatisticsViewModel] LoadAsync started. Kind={kind}, forceRefresh={forceRefresh}");

            if (kind == StatisticsDatabaseKind.ChildResults)
            {
                NoteStats.Clear();
                SessionStats.Clear();
                MasteryNotes.Clear();
                LastUpdatedUtc = null;
                IsLoading = false;
                return;
            }

            StatisticsDbFingerprint fingerprint;
            try
            {
                fingerprint = await StatisticsCacheService.GetFingerprintAsync(
                    kind, _noteDatabase, _sessionDatabase);
            }
            catch (Exception ex)
            {
                Utils.Log($"[NoteStatisticsViewModel] Fingerprint error: {ex}");
                IsLoading = true;
                await ComputeAndApplyAsync(kind, loadGeneration, forceRefresh: true);
                return;
            }

            if (!forceRefresh
                && _statisticsCache.TryGet(kind, out var cached)
                && cached is not null
                && StatisticsCacheService.IsValid(cached, fingerprint))
            {
                Utils.Log("[NoteStatisticsViewModel] Using valid cache.");
                ApplyCacheEntry(cached);
                _ = BackgroundRevalidateAsync(kind, fingerprint, loadGeneration);
                return;
            }

            IsLoading = true;
            await ComputeAndApplyAsync(kind, loadGeneration, forceRefresh);
        }

        private async Task BackgroundRevalidateAsync(
            StatisticsDatabaseKind kind,
            StatisticsDbFingerprint displayedFingerprint,
            int loadGeneration)
        {
            try
            {
                var fresh = await Task.Run(() =>
                    StatisticsCacheService.ComputeAsync(kind, _noteDatabase, _sessionDatabase));

                if (loadGeneration != Volatile.Read(ref _loadGeneration))
                    return;

                if (fresh.Fingerprint.Matches(displayedFingerprint))
                    return;

                _statisticsCache.Store(fresh);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (loadGeneration == Volatile.Read(ref _loadGeneration))
                        ApplyCacheEntry(fresh);
                });
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[NoteStatisticsViewModel] Background revalidate: {ex}");
            }
        }

        private async Task ComputeAndApplyAsync(
            StatisticsDatabaseKind kind,
            int loadGeneration,
            bool forceRefresh)
        {
            try
            {
                if (forceRefresh)
                    _statisticsCache.Invalidate(kind);

                var entry = await Task.Run(() =>
                    StatisticsCacheService.ComputeAsync(kind, _noteDatabase, _sessionDatabase));

                if (loadGeneration != Volatile.Read(ref _loadGeneration))
                    return;

                _statisticsCache.Store(entry);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (loadGeneration == Volatile.Read(ref _loadGeneration))
                        ApplyCacheEntry(entry);
                });
                Utils.Log("[NoteStatisticsViewModel] LoadAsync completed (computed).");
            }
            catch (Exception ex)
            {
                Utils.Log($"[NoteStatisticsViewModel] LoadAsync exception: {ex}");
            }
            finally
            {
                if (loadGeneration == Volatile.Read(ref _loadGeneration))
                    IsLoading = false;
            }
        }

        private void DecorateNoteStat(NoteStat stat)
        {
            stat.ContrastingTextColor = _themeService.ContrastingTextColor;
            MasteryEvaluator.RefreshMasteredFields(stat, _session);
        }

        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(NoteSessionService.LowestNote)
                or nameof(NoteSessionService.HighestNote)
                or nameof(NoteSessionService.Key)
                or nameof(NoteSessionService.Instrument)
                or nameof(NoteSessionService.SelectedScale)
                or nameof(NoteSessionService.EffectiveScale)
                or nameof(NoteSessionService.GenerationScale))
            {
                if (IsMasteryDatabase)
                    _ = LoadAsync(forceRefresh: true);
                return;
            }

            if (e.PropertyName is not (nameof(NoteSessionService.MasteredMethod)
                or nameof(NoteSessionService.StreakCrit)
                or nameof(NoteSessionService.CorrectThreshold)
                or nameof(NoteSessionService.MinCorrectCount)
                or nameof(NoteSessionService.OmitMsAvgThreshold)
                or nameof(NoteSessionService.ChildLevel)))
                return;

            RedecorateMasteredColumn();
            if (IsMasteryDatabase)
                _ = LoadAsync(forceRefresh: true);
        }

        private void RedecorateMasteredColumn()
        {
            if (NoteStats.Count == 0)
                return;

            foreach (var stat in NoteStats)
                DecorateNoteStat(stat);

            OnPropertyChanged(nameof(MasteredHeader));
            if (_noteCurrentSortColumn == "Mastered")
                SortNoteStatsByColumn("Mastered");
            else
                OnPropertyChanged(nameof(MasteredSortIndicator));
        }

        private void ApplyMasterySnapshot(NoteMasterySnapshot snapshot)
        {
            _masterySnapshot = snapshot;
            string? selectedName = SelectedMasteryNote?.WrittenNoteName;
            MasteryNotes.Clear();
            foreach (var note in snapshot.Notes)
                MasteryNotes.Add(note);

            SelectedMasteryNote = selectedName is null
                ? null
                : MasteryNotes.FirstOrDefault(n =>
                    string.Equals(n.WrittenNoteName, selectedName, StringComparison.OrdinalIgnoreCase));

            LastUpdatedUtc = snapshot.ComputedUtc;
            OnPropertyChanged(nameof(MasterySummaryText));
            OnPropertyChanged(nameof(MasteryMasteredCountText));
            OnPropertyChanged(nameof(MasteryImprovingCountText));
            OnPropertyChanged(nameof(MasteryNeedsPracticeCountText));
            OnPropertyChanged(nameof(MasteryNotTriedCountText));
            OnPropertyChanged(nameof(PreferBassClef));
            MasteryNotesChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Raised when mastery staff notes should be redrawn.</summary>
        public event EventHandler? MasteryNotesChanged;

        private void ApplyCacheEntry(StatisticsCacheEntry entry)
        {
            LastUpdatedUtc = entry.LastComputedUtc;

            if (entry.Kind == StatisticsDatabaseKind.Note && entry.NoteData is not null)
            {
                NoteStats.Clear();
                foreach (var stat in entry.NoteData.Stats)
                {
                    DecorateNoteStat(stat);
                    NoteStats.Add(stat);
                }

                SessionStats.Clear();
            }
            else if (entry.Kind == StatisticsDatabaseKind.Session && entry.SessionData is not null)
            {
                SessionStats.Clear();
                foreach (var stat in entry.SessionData.Stats)
                {
                    stat.ContrastingTextColor = _themeService.ContrastingTextColor;
                    SessionStats.Add(stat);
                    Utils.Log($"SessionStat: Dt={stat.Dt}, Key={stat.Key}, Tune={stat.Tune}, Instrument={stat.Instrument}, What={stat.What}, Hi={stat.Hi}, Lo={stat.Lo}, Pc={stat.Pc}");
                    if (stat.Key == null || stat.Tune == null || stat.Instrument == null || stat.What == null || stat.Hi == null || stat.Lo == null)
                        Utils.Log("[WARNING] Null property detected in SessionStat!");
                }

                NoteStats.Clear();
            }
            else
            {
                NoteStats.Clear();
                SessionStats.Clear();
            }
        }

        private string _selectedDatabase = Preferences.Get("musicmate.SelectedStatsDb", "Note");

        public string SelectedDatabase
        {
            get => _selectedDatabase;
            set
            {
                if (_selectedDatabase != value)
                {
                    _selectedDatabase = value;
                    Preferences.Set("musicmate.SelectedStatsDb", value);
                    OnPropertyChanged(nameof(SelectedDatabase));
                    OnPropertyChanged(nameof(IsNoteDatabase));
                    OnPropertyChanged(nameof(IsSessionDatabase));
                    OnPropertyChanged(nameof(IsChildResultsDatabase));
                    OnPropertyChanged(nameof(IsMasteryDatabase));
                    OnPropertyChanged(nameof(ShowDatabaseClearButtons));
                    _ = LoadAsyncOnMainThread();
                }
            }
        }

        /// <summary>
        /// Clear/Delete apply to Note, Session, and Child Results databases — not Mastery
        /// (Mastery is a derived view over note stats, not its own clearable store).
        /// </summary>
        public bool ShowDatabaseClearButtons => !IsMasteryDatabase;

        private async Task LoadAsyncOnMainThread()
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await LoadAsync();
                });
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[NoteStatisticsViewModel] Error loading stats: {ex}");
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Sort indicator properties for Note Stats
        public string NoteSortIndicator => _noteCurrentSortColumn == "Note" ? (_noteIsAscending ? "▲" : "▼") : "";
        public string CorrectSortIndicator => _noteCurrentSortColumn == "Correct" ? (_noteIsAscending ? "▲" : "▼") : "";
        public string WrongSortIndicator => _noteCurrentSortColumn == "Wrong" ? (_noteIsAscending ? "▲" : "▼") : "";
        public string PercentCorrectSortIndicator => _noteCurrentSortColumn == "PercentCorrect" ? (_noteIsAscending ? "▲" : "▼") : "";
        public string MsAvgSortIndicator => _noteCurrentSortColumn == "MsAvg" ? (_noteIsAscending ? "▲" : "▼") : "";
        public string StreakSortIndicator => _noteCurrentSortColumn == "Streak" ? (_noteIsAscending ? "▲" : "▼") : "";
        public string MasteredSortIndicator => _noteCurrentSortColumn == "Mastered" ? (_noteIsAscending ? "▲" : "▼") : "";
        public string MasteredHeader => "Mastered" + MasteredSortIndicator;

        // Sort indicator properties for Session Stats
        public string DateSortIndicator => _sessionCurrentSortColumn == "Date" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string LevelSortIndicator => _sessionCurrentSortColumn == "Level" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string CorrectPercentSortIndicator => _sessionCurrentSortColumn == "CorrectPercent" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string PchSortIndicator => _sessionCurrentSortColumn == "Pch" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string TmgSortIndicator => _sessionCurrentSortColumn == "Tmg" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string OvrlSortIndicator => _sessionCurrentSortColumn == "Ovrl" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string PitchRightSortIndicator => _sessionCurrentSortColumn == "PitchRight" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string PitchWrongSortIndicator => _sessionCurrentSortColumn == "PitchWrong" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string TimingRightSortIndicator => _sessionCurrentSortColumn == "TimingRight" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string TimingWrongSortIndicator => _sessionCurrentSortColumn == "TimingWrong" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string OverallRightSortIndicator => _sessionCurrentSortColumn == "OverallRight" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string OverallWrongSortIndicator => _sessionCurrentSortColumn == "OverallWrong" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string RestRightSortIndicator => _sessionCurrentSortColumn == "RestRight" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string RestWrongSortIndicator => _sessionCurrentSortColumn == "RestWrong" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string KeySortIndicator => _sessionCurrentSortColumn == "Key" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string WhatSortIndicator => _sessionCurrentSortColumn == "What" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string RandSortIndicator => _sessionCurrentSortColumn == "Rand" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string AccPctSortIndicator => _sessionCurrentSortColumn == "AccPct" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string HiSortIndicator => _sessionCurrentSortColumn == "Hi" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string LoSortIndicator => _sessionCurrentSortColumn == "Lo" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string InstrumentSortIndicator => _sessionCurrentSortColumn == "Instrument" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string TempoSortIndicator => _sessionCurrentSortColumn == "Tempo" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string TempoCVSortIndicator => _sessionCurrentSortColumn == "TempoCV" ? (_sessionIsAscending ? "▲" : "▼") : "";
        public string TempoSDSortIndicator => _sessionCurrentSortColumn == "TempoSD" ? (_sessionIsAscending ? "▲" : "▼") : "";

        // Combined header label text for session columns (text + arrow in one binding)
        public string DateHeader => "Date " + DateSortIndicator;
        public string LevelHeader => "Level " + LevelSortIndicator;
        public string CorrectPercentHeader => "Pc% " + CorrectPercentSortIndicator;
        public string PchHeader => "Pch " + PchSortIndicator;
        public string TmgHeader => "Tmg " + TmgSortIndicator;
        public string OvrlHeader => "Ovrl " + OvrlSortIndicator;
        public string PitchRightHeader => "P+ " + PitchRightSortIndicator;
        public string PitchWrongHeader => "P− " + PitchWrongSortIndicator;
        public string TimingRightHeader => "T+ " + TimingRightSortIndicator;
        public string TimingWrongHeader => "T− " + TimingWrongSortIndicator;
        public string OverallRightHeader => "O+ " + OverallRightSortIndicator;
        public string OverallWrongHeader => "O− " + OverallWrongSortIndicator;
        public string RestRightHeader => "R+ " + RestRightSortIndicator;
        public string RestWrongHeader => "R− " + RestWrongSortIndicator;
        public string KeyHeader => "Key " + KeySortIndicator;
        public string WhatHeader => "What " + WhatSortIndicator;
        public string RandHeader => "Rand " + RandSortIndicator;
        public string AccPctHeader => "Acc% " + AccPctSortIndicator;
        public string HiHeader => "Hi " + HiSortIndicator;
        public string LoHeader => "Lo " + LoSortIndicator;
        public string InstrumentHeader => "Inst " + InstrumentSortIndicator;
        public string TempoHeader => "Detected BPM " + TempoSortIndicator;
        public string TempoCVHeader => "Detected BPM CV " + TempoCVSortIndicator;
        public string TempoSDHeader => "Detected BPM SD " + TempoSDSortIndicator;

        // Returns a sort key for a note name such as "C#4" or "Bb3".
        // Priority: octave (largest), then letter (C<D<E<F<G<A<B), then accidental (flat < natural < sharp).
        private static (int octave, int letter, int accidental) NoteNameSortKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return (0, 0, 0);
            int i = 0;
            int letter = "CDEFGAB".IndexOf(char.ToUpper(name[i]));
            if (letter < 0) letter = 0;
            i++;
            int accidental = 0;
            if (i < name.Length && (name[i] == '#' || name[i] == 'b'))
            {
                accidental = name[i] == '#' ? 1 : -1;
                i++;
            }
            int octave = 0;
            if (i < name.Length && int.TryParse(name[i..], out int o))
                octave = o;
            return (octave, letter, accidental);
        }

        private void SortNoteStatsByColumn(string columnName)
        {
            // Toggle sort direction if same column, otherwise default to ascending
            if (_noteCurrentSortColumn == columnName)
                _noteIsAscending = !_noteIsAscending;
            else
            {
                _noteCurrentSortColumn = columnName;
                _noteIsAscending = true;
            }

            var sorted = (columnName switch
            {
                "Note" => _noteIsAscending
                    ? NoteStats.OrderBy(s => NoteNameSortKey(s.WrittenName))
                    : NoteStats.OrderByDescending(s => NoteNameSortKey(s.WrittenName)),
                "Correct" => _noteIsAscending
                    ? NoteStats.OrderBy(s => s.Correct)
                    : NoteStats.OrderByDescending(s => s.Correct),
                "Wrong" => _noteIsAscending
                    ? NoteStats.OrderBy(s => s.Wrong)
                    : NoteStats.OrderByDescending(s => s.Wrong),
                "PercentCorrect" => _noteIsAscending
                    ? NoteStats.OrderBy(s => s.PercentCorrect)
                    : NoteStats.OrderByDescending(s => s.PercentCorrect),
                "MsAvg" => _noteIsAscending
                    ? NoteStats.OrderBy(s => s.MsAverage)
                    : NoteStats.OrderByDescending(s => s.MsAverage),
                "Streak" => _noteIsAscending
                    ? NoteStats.OrderBy(s => s.Streak)
                    : NoteStats.OrderByDescending(s => s.Streak),
                "Mastered" => _noteIsAscending
                    ? NoteStats.OrderBy(s => s.MasteredDisplay)
                    : NoteStats.OrderByDescending(s => s.MasteredDisplay),
                _ => NoteStats.OrderBy(s => s.WrittenName) // Default to Note name ordering
            }).ToList(); // Materialize the sequence before clearing

            NoteStats.Clear();
            foreach (var stat in sorted)
                NoteStats.Add(stat);

            // Update all sort indicators
            OnPropertyChanged(nameof(NoteSortIndicator));
            OnPropertyChanged(nameof(CorrectSortIndicator));
            OnPropertyChanged(nameof(WrongSortIndicator));
            OnPropertyChanged(nameof(PercentCorrectSortIndicator));
            OnPropertyChanged(nameof(MsAvgSortIndicator));
            OnPropertyChanged(nameof(StreakSortIndicator));
            OnPropertyChanged(nameof(MasteredSortIndicator));
            OnPropertyChanged(nameof(MasteredHeader));
        }

        private void SortSessionStatsByColumn(string columnName)
        {
            // Toggle sort direction if same column, otherwise default to ascending
            if (_sessionCurrentSortColumn == columnName)
                _sessionIsAscending = !_sessionIsAscending;
            else
            {
                _sessionCurrentSortColumn = columnName;
                _sessionIsAscending = true;
            }

            var sorted = (columnName switch
            {
                "Date" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Dt)
                    : SessionStats.OrderByDescending(s => s.Dt),
                "Level" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Level)
                    : SessionStats.OrderByDescending(s => s.Level),
                "CorrectPercent" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Pc)
                    : SessionStats.OrderByDescending(s => s.Pc),
                "Pch" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Pch)
                    : SessionStats.OrderByDescending(s => s.Pch),
                "Tmg" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Tmg)
                    : SessionStats.OrderByDescending(s => s.Tmg),
                "Ovrl" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Ovrl)
                    : SessionStats.OrderByDescending(s => s.Ovrl),
                "PitchRight" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.PitchRightCount)
                    : SessionStats.OrderByDescending(s => s.PitchRightCount),
                "PitchWrong" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.PitchWrongCount)
                    : SessionStats.OrderByDescending(s => s.PitchWrongCount),
                "TimingRight" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.TimingRightCount)
                    : SessionStats.OrderByDescending(s => s.TimingRightCount),
                "TimingWrong" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.TimingWrongCount)
                    : SessionStats.OrderByDescending(s => s.TimingWrongCount),
                "OverallRight" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.OverallRightCount)
                    : SessionStats.OrderByDescending(s => s.OverallRightCount),
                "OverallWrong" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.OverallWrongCount)
                    : SessionStats.OrderByDescending(s => s.OverallWrongCount),
                "RestRight" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.RestRightCount)
                    : SessionStats.OrderByDescending(s => s.RestRightCount),
                "RestWrong" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.RestWrongCount)
                    : SessionStats.OrderByDescending(s => s.RestWrongCount),
                "Key" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Key)
                    : SessionStats.OrderByDescending(s => s.Key),
                "What" or "Scale" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.What)
                    : SessionStats.OrderByDescending(s => s.What),
                "Rand" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Rand)
                    : SessionStats.OrderByDescending(s => s.Rand),
                "AccPct" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.AccPct)
                    : SessionStats.OrderByDescending(s => s.AccPct),
                "Hi" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Hi)
                    : SessionStats.OrderByDescending(s => s.Hi),
                "Lo" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Lo)
                    : SessionStats.OrderByDescending(s => s.Lo),
                "Instrument" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Instrument)
                    : SessionStats.OrderByDescending(s => s.Instrument),
                "Tempo" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Tp)
                    : SessionStats.OrderByDescending(s => s.Tp),
                "TempoCV" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Cf)
                    : SessionStats.OrderByDescending(s => s.Cf),
                "TempoSD" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Ts)
                    : SessionStats.OrderByDescending(s => s.Ts),
                _ => SessionStats.OrderBy(s => s.Dt) // Default to Date ordering
            }).ToList(); // Materialize the sequence before clearing

            SessionStats.Clear();
            foreach (var stat in sorted)
                SessionStats.Add(stat);

            // Update all sort indicators
            OnPropertyChanged(nameof(DateSortIndicator));
            OnPropertyChanged(nameof(LevelSortIndicator));
            OnPropertyChanged(nameof(CorrectPercentSortIndicator));
            OnPropertyChanged(nameof(PchSortIndicator));
            OnPropertyChanged(nameof(TmgSortIndicator));
            OnPropertyChanged(nameof(OvrlSortIndicator));
            OnPropertyChanged(nameof(PitchRightSortIndicator));
            OnPropertyChanged(nameof(PitchWrongSortIndicator));
            OnPropertyChanged(nameof(TimingRightSortIndicator));
            OnPropertyChanged(nameof(TimingWrongSortIndicator));
            OnPropertyChanged(nameof(OverallRightSortIndicator));
            OnPropertyChanged(nameof(OverallWrongSortIndicator));
            OnPropertyChanged(nameof(RestRightSortIndicator));
            OnPropertyChanged(nameof(RestWrongSortIndicator));
            OnPropertyChanged(nameof(KeySortIndicator));
            OnPropertyChanged(nameof(WhatSortIndicator));
            OnPropertyChanged(nameof(RandSortIndicator));
            OnPropertyChanged(nameof(AccPctSortIndicator));
            OnPropertyChanged(nameof(HiSortIndicator));
            OnPropertyChanged(nameof(LoSortIndicator));
            OnPropertyChanged(nameof(InstrumentSortIndicator));
            OnPropertyChanged(nameof(TempoSortIndicator));
            OnPropertyChanged(nameof(TempoCVSortIndicator));
            OnPropertyChanged(nameof(TempoSDSortIndicator));
            OnPropertyChanged(nameof(DateHeader));
            OnPropertyChanged(nameof(LevelHeader));
            OnPropertyChanged(nameof(CorrectPercentHeader));
            OnPropertyChanged(nameof(PchHeader));
            OnPropertyChanged(nameof(TmgHeader));
            OnPropertyChanged(nameof(OvrlHeader));
            OnPropertyChanged(nameof(PitchRightHeader));
            OnPropertyChanged(nameof(PitchWrongHeader));
            OnPropertyChanged(nameof(TimingRightHeader));
            OnPropertyChanged(nameof(TimingWrongHeader));
            OnPropertyChanged(nameof(OverallRightHeader));
            OnPropertyChanged(nameof(OverallWrongHeader));
            OnPropertyChanged(nameof(RestRightHeader));
            OnPropertyChanged(nameof(RestWrongHeader));
            OnPropertyChanged(nameof(KeyHeader));
            OnPropertyChanged(nameof(WhatHeader));
            OnPropertyChanged(nameof(RandHeader));
            OnPropertyChanged(nameof(AccPctHeader));
            OnPropertyChanged(nameof(HiHeader));
            OnPropertyChanged(nameof(LoHeader));
            OnPropertyChanged(nameof(InstrumentHeader));
            OnPropertyChanged(nameof(TempoHeader));
            OnPropertyChanged(nameof(TempoCVHeader));
            OnPropertyChanged(nameof(TempoSDHeader));
        }
    }
}

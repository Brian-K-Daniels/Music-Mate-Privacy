using musicmate.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using musicmate.Utilities;
using System.Windows.Input;

namespace musicmate.ViewModels
{  
    public class NoteStatisticsViewModel : INotifyPropertyChanged
    {
        private readonly NoteDatabase _noteDatabase;
        private readonly SessionDatabase _sessionDatabase;
        private readonly ThemeService _themeService;
        private readonly NoteSessionService _session;
        public Color PanelBackgroundColor => _themeService.PanelBackgroundColor;
        public Color ContrastingTextColor => _themeService.ContrastingTextColor;
        public bool IsNoteDatabase => SelectedDatabase == "Note";
        public bool IsSessionDatabase => SelectedDatabase == "Session";
        public bool IsChildResultsDatabase => SelectedDatabase == "Child Results";
        public ObservableCollection<string> DatabaseOptions { get; } = new() { "Note", "Session", "Child Results" };
        // Remove color properties from here; use ThemeService for colors in the view.
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<NoteStat> NoteStats { get; } = new();
        public ObservableCollection<SessionStat> SessionStats { get; } = new();

        // Sorting state for Note Stats
        private string _noteCurrentSortColumn = "";
        private bool _noteIsAscending = true;

        // Sorting state for Session Stats
        private string _sessionCurrentSortColumn = "";
        private bool _sessionIsAscending = true;

        // Commands
        public ICommand SortNoteStatsCommand { get; }
        public ICommand SortSessionStatsCommand { get; }

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

        private double _averageWrong = 0.0;
        public double AverageWrong
        {
            get => _averageWrong;
            private set
            {
                if (Math.Abs(_averageWrong - value) > 0.0001)
                {
                    _averageWrong = value;
                    OnPropertyChanged(nameof(AverageWrong));
                }
            }
        }

        public NoteStatisticsViewModel(NoteDatabase noteDatabase, SessionDatabase sessionDatabase, ThemeService themeService, NoteSessionService session)
        {
            _noteDatabase = noteDatabase;
            _sessionDatabase = sessionDatabase;
            _themeService = themeService;
            _session = session;

            SortNoteStatsCommand = new Command<string>(SortNoteStatsByColumn);
            SortSessionStatsCommand = new Command<string>(SortSessionStatsByColumn);
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

        public async Task LoadAsync()
        {
            Utils.Log($"[NoteStatisticsViewModel] LoadAsync started. IsNoteDatabase={IsNoteDatabase}");
            IsLoading = true;
            try
            {
                if (IsNoteDatabase)
                {
                    var stats = await _noteDatabase.GetAllAsync();
                    Utils.Log($"[NoteStatisticsViewModel] Loaded {stats.Count()} Statistics.");
                    NoteStats.Clear();
                    foreach (var stat in stats.OrderBy(s => s.PercentCorrect))
                    {
                        stat.ContrastingTextColor = _themeService.ContrastingTextColor;
                        NoteStats.Add(stat);
                    }
                    // compute average wrong for display
                    if (NoteStats.Count > 0)
                        AverageWrong = NoteStats.Average(n => (double)n.Wrong);
                    else
                        AverageWrong = 0.0;
                    // Clear session stats if switching from session to note
                    SessionStats.Clear();
                }
                else if (IsSessionDatabase)
                {
                    var stats = await _sessionDatabase.GetAllAsync();
                    Utils.Log($"[NoteStatisticsViewModel] Loaded {stats.Count()} session stats.");
                    SessionStats.Clear();
                    foreach (var stat in stats)
                    {
                        stat.ContrastingTextColor = _themeService.ContrastingTextColor;
                        SessionStats.Add(stat);
                        Utils.Log($"SessionStat: Dt={stat.Dt}, Key={stat.Key}, Tune={stat.Tune}, Instrument={stat.Instrument}, Sc={stat.Sc}, Hi={stat.Hi}, Lo={stat.Lo}, Pc={stat.Pc}");
                        if (stat.Key == null || stat.Tune == null || stat.Instrument == null || stat.Sc == null || stat.Hi == null || stat.Lo == null)
                            Utils.Log("[WARNING] Null property detected in SessionStat!");
                    }
                    // Clear note stats if switching from note to session
                    NoteStats.Clear();
                }
                else
                {
                    NoteStats.Clear();
                    SessionStats.Clear();
                }
                Utils.Log("[NoteStatisticsViewModel] LoadAsync completed.");
            }
            catch (Exception ex)
            {
                Utils.Log($"[NoteStatisticsViewModel] LoadAsync exception: {ex}");
            }
            finally
            {
                IsLoading = false;
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
                    _ = LoadAsyncOnMainThread();
                }
            }
        }

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
                System.Diagnostics.Debug.WriteLine($"[NoteStatisticsViewModel] Error loading stats: {ex}");
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
        public string ScaleSortIndicator => _sessionCurrentSortColumn == "Scale" ? (_sessionIsAscending ? "▲" : "▼") : "";
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
        public string ScaleHeader => "Scale " + ScaleSortIndicator;
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
                "Scale" => _sessionIsAscending
                    ? SessionStats.OrderBy(s => s.Sc)
                    : SessionStats.OrderByDescending(s => s.Sc),
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
            OnPropertyChanged(nameof(ScaleSortIndicator));
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
            OnPropertyChanged(nameof(ScaleHeader));
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

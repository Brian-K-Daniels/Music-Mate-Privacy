using musicmate.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using musicmate.Utilities;

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
        public ObservableCollection<string> DatabaseOptions { get; } = new() { "Note", "Session" };
        // Remove color properties from here; use ThemeService for colors in the view.
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<NoteStat> NoteStats { get; } = new();
        public ObservableCollection<SessionStat> SessionStats { get; } = new();
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
                else
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
                Utils.Log("[NoteStatisticsViewModel] LoadAsync completed.");
            }
            catch (Exception ex)
            {
                Utils.Log($"[NoteStatisticsViewModel] LoadAsync exception: {ex}");
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
    }
}

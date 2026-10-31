using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.ViewModels;

public sealed class NoteAttemptsDebugPageViewModel : INotifyPropertyChanged
{
    private readonly NoteAttemptDatabase _db;
    private readonly ThemeService _theme;
    private string _statusText = "Loading…";
    private string _histogramText = string.Empty;
    private string _databasePathText = string.Empty;
    private bool _isBusy;

    public NoteAttemptsDebugPageViewModel(NoteAttemptDatabase db, ThemeService theme)
    {
        _db = db;
        _theme = theme;
        _theme.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null
                or nameof(ThemeService.PanelBackgroundColor)
                or nameof(ThemeService.ContrastingTextColor))
            {
                OnPropertyChanged(nameof(PanelBackgroundColor));
                OnPropertyChanged(nameof(ContrastingTextColor));
            }
        };

        RefreshCommand = new Command(async () => await LoadAsync(), () => !IsBusy);
        ClearCommand = new Command(async () => await ClearAsync(), () => !IsBusy);
        DatabasePathText = _db.DatabasePath;
    }

    public ObservableCollection<NoteAttemptRowViewModel> Rows { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand ClearCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string HistogramText
    {
        get => _histogramText;
        private set
        {
            if (_histogramText == value) return;
            _histogramText = value;
            OnPropertyChanged(nameof(HistogramText));
        }
    }

    public string DatabasePathText
    {
        get => _databasePathText;
        private set
        {
            if (_databasePathText == value) return;
            _databasePathText = value;
            OnPropertyChanged(nameof(DatabasePathText));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged(nameof(IsBusy));
            ((Command)RefreshCommand).ChangeCanExecute();
            ((Command)ClearCommand).ChangeCanExecute();
        }
    }

    public Color PanelBackgroundColor => _theme.PanelBackgroundColor;
    public Color ContrastingTextColor => _theme.ContrastingTextColor;
    public bool HasNoRows => Rows.Count == 0;

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            await _db.InitializeAsync();
            var all = await _db.GetAllAsync() ?? new List<NoteAttempt>();
            // Newest session first; within a session, conductor time order (t=expected/…).
            var ordered = all
                .GroupBy(a => a.SessionId ?? string.Empty)
                .OrderByDescending(g => g.Max(a => a.AttemptId))
                .SelectMany(g => g
                    .OrderBy(a => a.ExpectedStartMs ?? a.ActualDetectedMs ?? double.MaxValue)
                    .ThenBy(a => a.ActualDetectedMs ?? double.MaxValue)
                    .ThenBy(a => a.AttemptId))
                .ToList();

            Rows.Clear();
            foreach (var attempt in ordered)
                Rows.Add(new NoteAttemptRowViewModel(attempt));
            OnPropertyChanged(nameof(HasNoRows));

            int wrong = ordered.Count(a => !a.OverallCorrect);
            int hadEarlyCandidate = ordered.Count(a =>
                NoteAttemptTimingDiagnostics.IsHadEarlyCandidateReason(a.WrongReason));
            StatusText = hadEarlyCandidate > 0
                ? $"{ordered.Count} attempt(s), {wrong} wrong, {hadEarlyCandidate} hadEarlyCandidate"
                : $"{ordered.Count} attempt(s), {wrong} wrong";

            var hist = ordered
                .Where(a => !a.OverallCorrect
                    || NoteAttemptTimingDiagnostics.IsHadEarlyCandidateReason(a.WrongReason))
                .GroupBy(a =>
                {
                    if (NoteAttemptTimingDiagnostics.IsHadEarlyCandidateReason(a.WrongReason))
                        return NoteAttemptTimingDiagnostics.HadEarlyCandidateReason;
                    return string.IsNullOrWhiteSpace(a.WrongReason) ? "(no reason)" : a.WrongReason;
                })
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}: {g.Count()}");

            HistogramText = hist.Any()
                ? "Reasons — " + string.Join(" · ", hist)
                : "Reasons — none";
        }
        catch (Exception ex)
        {
            StatusText = $"Load failed: {ex.Message}";
            HistogramText = string.Empty;
            Rows.Clear();
            OnPropertyChanged(nameof(HasNoRows));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ClearAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            await _db.InitializeAsync();
            await _db.ClearAllAsync();
            ServiceHelper.GetService<StatisticsCacheService>()?.InvalidateNoteStats();
            ServiceHelper.GetService<NoteMasteryService>()?.Invalidate();
            Rows.Clear();
            OnPropertyChanged(nameof(HasNoRows));
            StatusText = "0 attempt(s), 0 wrong";
            HistogramText = "Reasons — none";
        }
        catch (Exception ex)
        {
            StatusText = $"Clear failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.ViewModels;

public sealed class NoteAttemptRowViewModel
{
    public NoteAttemptRowViewModel(NoteAttempt attempt)
    {
        AttemptId = attempt.AttemptId;
        Header = $"#{attempt.AttemptId}  {attempt.DateTime:u}  sess={Truncate(attempt.SessionId, 8)}";
        Detail = BuildDetail(attempt);
        IsWrong = !attempt.OverallCorrect;
    }

    public int AttemptId { get; }
    public string Header { get; }
    public string Detail { get; }
    public bool IsWrong { get; }

    private static string BuildDetail(NoteAttempt a)
    {
        var sb = new StringBuilder();
        sb.Append(a.IsRest ? "REST" : a.ExpectedWrittenNoteName);
        if (!string.IsNullOrEmpty(a.ExpectedDuration))
            sb.Append(' ').Append(a.ExpectedDuration);
        sb.Append(" → ").Append(string.IsNullOrEmpty(a.ActualDetectedNoteName) ? "-" : a.ActualDetectedNoteName);
        sb.Append(" | P=").Append(a.PitchCorrect ? "ok" : "no");
        sb.Append(" T=").Append(a.TimingCorrect switch
        {
            true => "ok",
            false => "no",
            null => "-",
        });
        sb.Append(" O=").Append(a.OverallCorrect ? "ok" : "no");
        if (!a.OverallCorrect && !string.IsNullOrEmpty(a.WrongReason))
            sb.Append(" | ").Append(a.WrongReason);
        if (a.PitchErrorCents != 0)
            sb.Append(" | ").Append(a.PitchErrorCents).Append('¢');
        if (a.TimingErrorMs.HasValue)
            sb.Append(" | Δ").Append(a.TimingErrorMs.Value.ToString("F0", CultureInfo.InvariantCulture)).Append("ms");
        if (a.ExpectedStartMs.HasValue || a.ActualDetectedMs.HasValue)
        {
            sb.Append(" | t=");
            sb.Append(a.ExpectedStartMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "-");
            sb.Append('/');
            sb.Append(a.ActualDetectedMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "-");
        }

        return sb.ToString();
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return "-";
        return s.Length <= max ? s : s[..max];
    }
}

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
            var ordered = all.OrderByDescending(a => a.AttemptId).ToList();

            Rows.Clear();
            foreach (var attempt in ordered)
                Rows.Add(new NoteAttemptRowViewModel(attempt));
            OnPropertyChanged(nameof(HasNoRows));

            int wrong = ordered.Count(a => !a.OverallCorrect);
            StatusText = $"{ordered.Count} attempt(s), {wrong} wrong";

            var hist = ordered
                .Where(a => !a.OverallCorrect)
                .GroupBy(a => string.IsNullOrWhiteSpace(a.WrongReason) ? "(no reason)" : a.WrongReason)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}: {g.Count()}");

            HistogramText = hist.Any()
                ? "Wrong reasons — " + string.Join(" · ", hist)
                : "Wrong reasons — none";
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
            HistogramText = "Wrong reasons — none";
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

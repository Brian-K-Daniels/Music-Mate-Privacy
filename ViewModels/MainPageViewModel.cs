using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using musicmate.Services; 
namespace musicmate.ViewModels;
public partial class MainPageViewModel : INotifyPropertyChanged
{
    private readonly NoteSessionService _sessionService;

    public MainPageViewModel(NoteSessionService sessionService)
    {
        _sessionService = sessionService;
        // Optionally, subscribe to property changes if you want to forward them
        _sessionService.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_sessionService.BpmStatsDisplay))
                OnPropertyChanged(nameof(BpmStatsDisplay));
            if (e.PropertyName == nameof(_sessionService.AutoStart))
                OnPropertyChanged(nameof(AutoStart));
        };
    }

    public string BpmStatsDisplay => _sessionService.BpmStatsDisplay;

    // Example property
    private string _someProperty = "";

    public string SomeProperty
    {
        get => _someProperty;
        set
        {
            if (_someProperty != value)
            {
                _someProperty = value;
                OnPropertyChanged(nameof(SomeProperty));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AutoStart
    {
        get => _sessionService.AutoStart;
        set
        {
            if (_sessionService.AutoStart != value)
            {
                _sessionService.AutoStart = value;
                OnPropertyChanged(nameof(AutoStart));
            }
        }
    }

    private string? _selectedTune;
    public string? SelectedTune
    {
        get => _selectedTune;
        set
        {
            if (_selectedTune != value)
            {
                _selectedTune = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTunerSelected));
            }
        }
    }

    public bool IsTunerSelected => SelectedTune == "Tuner";

    // Example properties for binding
    public ObservableCollection<object> FeedbackViewModels { get; } = new();

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

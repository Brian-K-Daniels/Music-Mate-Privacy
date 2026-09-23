using System.Collections.ObjectModel;
using System.ComponentModel;
using musicmate.Diagnostics;
using musicmate.Services;

namespace musicmate.ViewModels;

public sealed class DebugLogCategoryItem : INotifyPropertyChanged
{
    private bool _isEnabled;

    public DebugLogCategoryItem(DebugLogCategory category, bool isEnabled)
    {
        Category = category;
        _isEnabled = isEnabled;
        DisplayName = DebugLogSettings.GetDisplayName(category);
        Description = DebugLogSettings.GetDescription(category);
    }

    public DebugLogCategory Category { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            DebugLogSettings.SetEnabled(Category, value);
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class DebugItemsPageViewModel : INotifyPropertyChanged
{
    private readonly ThemeService _theme;

    public DebugItemsPageViewModel(ThemeService theme)
    {
        _theme = theme;
        _theme.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThemeService.PanelBackgroundColor))
            {
                OnPropertyChanged(nameof(PanelBackgroundColor));
                OnPropertyChanged(nameof(ContrastingTextColor));
            }
        };

        foreach (var category in DebugLogSettings.AllCategories)
        {
            Categories.Add(new DebugLogCategoryItem(category, DebugLogSettings.IsEnabled(category)));
        }
    }

    public ObservableCollection<DebugLogCategoryItem> Categories { get; } = new();

    public Color PanelBackgroundColor => _theme.PanelBackgroundColor;

    public Color ContrastingTextColor => _theme.ContrastingTextColor;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

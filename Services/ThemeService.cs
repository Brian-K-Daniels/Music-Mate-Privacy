using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace musicmate.Services;

public class ThemeService : INotifyPropertyChanged
{
    public const string ColorPreferencePrefix = "musicmate.color.";

    private static readonly IReadOnlyDictionary<AppColorTarget, string> FactoryDefaultHex = new Dictionary<AppColorTarget, string>
    {
        [AppColorTarget.MainBackground] = "#FFFFFF",
        [AppColorTarget.PanelBackground] = "#FFFFFF",
        [AppColorTarget.SecondPanelBackground] = "#F8F8FF",
        [AppColorTarget.Text] = "#000000",
        [AppColorTarget.HeadingText] = "#8B4513",
        [AppColorTarget.Slider] = "#8B4513",
        [AppColorTarget.ButtonBackground] = "#8B4513",
        [AppColorTarget.ButtonText] = "#FFFFFF",
        [AppColorTarget.PickerBackground] = "#FFFFFF",
        [AppColorTarget.PickerText] = "#000000",
        [AppColorTarget.PickerBorder] = "#8B4513",
        [AppColorTarget.EntryBackground] = "#FFFFFF",
        [AppColorTarget.EntryText] = "#000000",
    };

    /// <summary>When set, preferences read/write use this dictionary instead of MAUI Preferences (tests).</summary>
    internal static Dictionary<string, string>? TestStore { get; set; }

    private readonly Dictionary<AppColorTarget, Color> _colors = new();

    public ThemeService()
    {
        foreach (var target in FactoryDefaultHex.Keys)
            _colors[target] = Color.FromArgb(FactoryDefaultHex[target]);
    }

    public Color CurrentNoteHighlightColor => Colors.Yellow.WithAlpha(0.95f);
    public Color CorrectNoteColor => Colors.LightGreen.WithAlpha(0.95f);
    public Color WrongNoteColor => Colors.LightPink.WithAlpha(0.95f);

    public Color MainBackgroundColor => GetColor(AppColorTarget.MainBackground);
    public Color PanelBackgroundColor
    {
        get => GetColor(AppColorTarget.PanelBackground);
        set => SetColor(AppColorTarget.PanelBackground, value);
    }

    public Color SecondPanelBackgroundColor => GetColor(AppColorTarget.SecondPanelBackground);
    public Color TextColor => GetEffectiveTextColor(AppColorTarget.Text, AppColorTarget.PanelBackground);
    public Color HeadingTextColor => GetEffectiveTextColor(AppColorTarget.HeadingText, AppColorTarget.PanelBackground);
    public Color SliderColor => GetColor(AppColorTarget.Slider);
    public Color ButtonBackgroundColor => GetColor(AppColorTarget.ButtonBackground);
    public Color ButtonTextColor => GetEffectiveTextColor(AppColorTarget.ButtonText, AppColorTarget.ButtonBackground);
    public Color PickerBackgroundColor => GetColor(AppColorTarget.PickerBackground);
    public Color PickerTextColor => GetEffectiveTextColor(AppColorTarget.PickerText, AppColorTarget.PickerBackground);
    public Color PickerBorderColor => GetColor(AppColorTarget.PickerBorder);
    public Color EntryBackgroundColor => GetColor(AppColorTarget.EntryBackground);
    public Color EntryTextColor => GetEffectiveTextColor(AppColorTarget.EntryText, AppColorTarget.EntryBackground);

    /// <summary>Readable body text on the main panel background.</summary>
    public Color ContrastingTextColor => GetEffectiveTextColor(AppColorTarget.Text, AppColorTarget.PanelBackground);

    /// <summary>Shell navigation bar background; paired with <see cref="ContrastingTextColor"/>.</summary>
    public Color ShellChromeBackgroundColor => PanelBackgroundColor;

    /// <summary>Shell navigation bar icons and title; contrast-checked against <see cref="ShellChromeBackgroundColor"/>.</summary>
    public Color ShellChromeForegroundColor => ContrastingTextColor;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ThemeColorsChanged;

    public static string GetDisplayName(AppColorTarget target) => target switch
    {
        AppColorTarget.MainBackground => "Main background",
        AppColorTarget.PanelBackground => "Panel background",
        AppColorTarget.SecondPanelBackground => "Second panel background",
        AppColorTarget.Text => "Text",
        AppColorTarget.HeadingText => "Heading text",
        AppColorTarget.Slider => "Slider",
        AppColorTarget.ButtonBackground => "Button background",
        AppColorTarget.ButtonText => "Button text",
        AppColorTarget.PickerBackground => "Picker background",
        AppColorTarget.PickerText => "Picker text",
        AppColorTarget.PickerBorder => "Picker border",
        AppColorTarget.EntryBackground => "Entry background",
        AppColorTarget.EntryText => "Entry text",
        _ => target.ToString(),
    };

    public static IReadOnlyList<AppColorTarget> AllColorTargets { get; } =
        Enum.GetValues<AppColorTarget>().Cast<AppColorTarget>().ToList();

    public Color GetColor(AppColorTarget target)
        => _colors.TryGetValue(target, out var color)
            ? color
            : Color.FromArgb(FactoryDefaultHex[target]);

    public Color GetFactoryDefaultColor(AppColorTarget target)
        => Color.FromArgb(FactoryDefaultHex[target]);

    /// <summary>Stored text color, adjusted for readability on its paired background when contrast is poor.</summary>
    public Color GetEffectiveTextColor(AppColorTarget textTarget, AppColorTarget backgroundTarget)
    {
        var text = GetColor(textTarget);
        var background = GetColor(backgroundTarget);
        return ThemeColorContrast.ResolveReadableText(text, background);
    }

    public void SetColor(AppColorTarget target, Color color)
    {
        var normalized = NormalizeOpaque(color);
        if (GetColor(target) == normalized)
            return;

        if (IsTextTarget(target))
        {
            var backgroundTarget = GetPairedBackgroundTarget(target);
            if (!ThemeColorContrast.HasReadableContrast(normalized, GetColor(backgroundTarget)))
                normalized = ThemeColorContrast.GetContrastingTextColor(GetColor(backgroundTarget));
        }

        _colors[target] = normalized;
        PersistColor(target, normalized);
        SyncLegacyPanelPreferences(target, normalized);
        RaiseColorPropertiesChanged(target);
    }

    public void LoadFromPreferences()
    {
        MigrateLegacyPanelColor();

        foreach (var target in AllColorTargets)
        {
            var key = PreferenceKey(target);
            var hex = GetPreference(key, FactoryDefaultHex[target]);
            _colors[target] = Color.FromArgb(hex);
        }

        RaiseAllColorPropertiesChanged();
        ApplyToShellIfAvailable();
    }

    public void PushToApplicationResources()
    {
        var resources = Application.Current?.Resources;
        if (resources == null)
            return;

        resources[ThemeResourceKeys.MainBackground] = MainBackgroundColor;
        resources[ThemeResourceKeys.PanelBackground] = PanelBackgroundColor;
        resources[ThemeResourceKeys.SecondPanelBackground] = SecondPanelBackgroundColor;
        resources[ThemeResourceKeys.Text] = TextColor;
        resources[ThemeResourceKeys.HeadingText] = HeadingTextColor;
        resources[ThemeResourceKeys.ContrastingText] = ContrastingTextColor;
        resources[ThemeResourceKeys.Slider] = SliderColor;
        resources[ThemeResourceKeys.ButtonBackground] = ButtonBackgroundColor;
        resources[ThemeResourceKeys.ButtonText] = ButtonTextColor;
        resources[ThemeResourceKeys.PickerBackground] = PickerBackgroundColor;
        resources[ThemeResourceKeys.PickerText] = PickerTextColor;
        resources[ThemeResourceKeys.PickerBorder] = PickerBorderColor;
        resources[ThemeResourceKeys.EntryBackground] = EntryBackgroundColor;
        resources[ThemeResourceKeys.EntryText] = EntryTextColor;
    }

    public void ResetAllToFactoryDefaults()
    {
        foreach (var target in AllColorTargets)
        {
            var factory = GetFactoryDefaultColor(target);
            _colors[target] = factory;
            PersistColor(target, factory);
        }

        SyncLegacyPanelPreferences(AppColorTarget.PanelBackground, PanelBackgroundColor);
        RaiseAllColorPropertiesChanged();
        ApplyToShellIfAvailable();
    }

    public void ApplyToShellIfAvailable()
    {
        try
        {
            PushToApplicationResources();

            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is Shell shell)
            {
                shell.BackgroundColor = ShellChromeBackgroundColor;
                shell.FlyoutBackgroundColor = SecondPanelBackgroundColor;
                Shell.SetTitleColor(shell, ShellChromeForegroundColor);
                Shell.SetForegroundColor(shell, ShellChromeForegroundColor);
                Shell.SetDisabledColor(shell, ShellChromeForegroundColor.WithAlpha(0.5f));
            }
        }
        catch
        {
            // Shell may not exist during early startup.
        }
    }

    private void MigrateLegacyPanelColor()
    {
        var legacyHex = TryGetPreference("StaffPanelColor")
            ?? TryGetPreference("musicmate.PanelBackgroundColor");
        if (string.IsNullOrWhiteSpace(legacyHex))
            return;

        var key = PreferenceKey(AppColorTarget.PanelBackground);
        if (!HasPreference(key))
            SetPreference(key, legacyHex);
    }

    private static bool IsTextTarget(AppColorTarget target) => target is
        AppColorTarget.Text or
        AppColorTarget.HeadingText or
        AppColorTarget.ButtonText or
        AppColorTarget.PickerText or
        AppColorTarget.EntryText;

    private static AppColorTarget GetPairedBackgroundTarget(AppColorTarget textTarget) => textTarget switch
    {
        AppColorTarget.ButtonText => AppColorTarget.ButtonBackground,
        AppColorTarget.PickerText => AppColorTarget.PickerBackground,
        AppColorTarget.EntryText => AppColorTarget.EntryBackground,
        _ => AppColorTarget.PanelBackground,
    };

    private static Color NormalizeOpaque(Color color)
        => new(color.Red, color.Green, color.Blue, 1f);

    private static string PreferenceKey(AppColorTarget target)
        => ColorPreferencePrefix + target;

    private static void PersistColor(AppColorTarget target, Color color)
        => SetPreference(PreferenceKey(target), color.ToHex());

    private static void SyncLegacyPanelPreferences(AppColorTarget target, Color color)
    {
        if (target != AppColorTarget.PanelBackground)
            return;

        var hex = color.ToHex();
        SetPreference("StaffPanelColor", hex);
        SetPreference("musicmate.PanelBackgroundColor", hex);
    }

    private void RaiseColorPropertiesChanged(AppColorTarget target)
    {
        switch (target)
        {
            case AppColorTarget.MainBackground:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MainBackgroundColor)));
                break;
            case AppColorTarget.PanelBackground:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PanelBackgroundColor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContrastingTextColor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextColor)));
                break;
            case AppColorTarget.SecondPanelBackground:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondPanelBackgroundColor)));
                break;
            case AppColorTarget.Text:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextColor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContrastingTextColor)));
                break;
            case AppColorTarget.HeadingText:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeadingTextColor)));
                break;
            case AppColorTarget.Slider:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SliderColor)));
                break;
            case AppColorTarget.ButtonBackground:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ButtonBackgroundColor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ButtonTextColor)));
                break;
            case AppColorTarget.ButtonText:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ButtonTextColor)));
                break;
            case AppColorTarget.PickerBackground:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickerBackgroundColor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickerTextColor)));
                break;
            case AppColorTarget.PickerText:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickerTextColor)));
                break;
            case AppColorTarget.PickerBorder:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickerBorderColor)));
                break;
            case AppColorTarget.EntryBackground:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryBackgroundColor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryTextColor)));
                break;
            case AppColorTarget.EntryText:
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryTextColor)));
                break;
        }

        ThemeColorsChanged?.Invoke(this, EventArgs.Empty);
        PushToApplicationResources();
    }

    private void RaiseAllColorPropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MainBackgroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PanelBackgroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondPanelBackgroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeadingTextColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SliderColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ButtonBackgroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ButtonTextColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickerBackgroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickerTextColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickerBorderColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryBackgroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryTextColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContrastingTextColor)));
        ThemeColorsChanged?.Invoke(this, EventArgs.Empty);
        PushToApplicationResources();
    }

    private static bool HasPreference(string key)
    {
        if (TestStore != null)
            return TestStore.ContainsKey(key);
        return Preferences.ContainsKey(key);
    }

    private static string? TryGetPreference(string key)
    {
        if (TestStore != null)
            return TestStore.TryGetValue(key, out var value) ? value : null;
        return Preferences.ContainsKey(key) ? Preferences.Get(key, string.Empty) : null;
    }

    private static string GetPreference(string key, string defaultValue)
    {
        if (TestStore != null)
            return TestStore.TryGetValue(key, out var value) ? value : defaultValue;
        return Preferences.Get(key, defaultValue);
    }

    private static void SetPreference(string key, string value)
    {
        if (TestStore != null)
            TestStore[key] = value;
        else
            Preferences.Set(key, value);
    }
}

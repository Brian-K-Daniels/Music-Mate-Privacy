using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Music-page time-signature selector: same meters as the former Settings picker,
/// persisted via <see cref="NoteSessionService.MeterTimeSignature"/>.
/// </summary>
[Collection("SessionPreferences")]
public class MusicPageTimeSignatureControlTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public MusicPageTimeSignatureControlTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose()
        => SessionPreferences.TestStore = null;

    [Fact]
    public void ControlOptions_MatchFormerSettingsCommonDisplayOptionsExactly()
    {
        Assert.Equal(
            TimeSignature.CommonDisplayOptions.ToArray(),
            TimeSignatureControlLogic.Options.ToArray());

        Assert.Equal(
            new[] { "2/2", "2/4", "3/4", "4/4", "5/4", "3/8", "6/8", "9/8", "12/8" },
            TimeSignatureControlLogic.Options.ToArray());
    }

    [Theory]
    [InlineData("4/4")]
    [InlineData("3/4")]
    [InlineData("6/8")]
    [InlineData("12/8")]
    public void SelectingOption_UpdatesDisplayAndPersistsPreference(string meter)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            MeterTimeSignature = "2/4",
        };

        string? next = TimeSignatureControlLogic.NormalizeSelection(meter);
        Assert.NotNull(next);
        Assert.True(TimeSignatureControlLogic.WouldChange(session.MeterTimeSignature, next));

        session.MeterTimeSignature = next!;

        Assert.Equal(meter, session.MeterTimeSignature);
        Assert.Equal(meter, session.GetDisplayTimeSignature());
        Assert.Equal(meter, SessionPreferences.Get("musicmate.TimeSignature", ""));
        Assert.Equal(
            TimeSignature.FromDisplayString(meter).TotalBeats,
            session.GetDisplayMeasureBeats(),
            precision: 3);
    }

    [Fact]
    public void NormalizeSelection_RejectsUnknownMeters()
    {
        Assert.Null(TimeSignatureControlLogic.NormalizeSelection("7/8"));
        Assert.Null(TimeSignatureControlLogic.NormalizeSelection(""));
        Assert.Null(TimeSignatureControlLogic.NormalizeSelection(null));
        Assert.False(TimeSignatureControlLogic.WouldChange("4/4", "7/8"));
    }

    [Fact]
    public void WouldChange_FalseWhenSameMeter()
    {
        Assert.False(TimeSignatureControlLogic.WouldChange("3/4", "3/4"));
        Assert.True(TimeSignatureControlLogic.WouldChange("3/4", "4/4"));
    }

    [Fact]
    public void SettingsPageXaml_NoLongerContainsTimeSignaturePicker()
    {
        string path = FindRepoFile(Path.Combine("Pages", "SettingsPage.xaml"));
        string text = File.ReadAllText(path);

        Assert.DoesNotContain("Text=\"Time signature\"", text);
        Assert.DoesNotContain("ItemsSource=\"{Binding MeterTimeSignatureOptions}\"", text);
        Assert.DoesNotContain("SelectedItem=\"{Binding MeterTimeSignature, Mode=TwoWay}\"", text);
    }

    [Fact]
    public void MusicPageXaml_HasTimeSignatureHitTargetAndSelector()
    {
        string path = FindRepoFile(Path.Combine("Pages", "MusicPage.xaml"));
        string text = File.ReadAllText(path);

        Assert.Contains("x:Name=\"TimeSignatureHitTarget\"", text);
        Assert.Contains("OnTimeSignatureHitTargetClicked", text);
        Assert.Contains("x:Name=\"TimeSignatureControlRow\"", text);
        Assert.Contains("x:Name=\"TimeSignatureOptionsGrid\"", text);
        Assert.Contains("<Button x:Name=\"TimeSignatureHitTarget\"", text);
    }

    [Fact]
    public void AboutHtml_DoesNotTellUsersToChangeTimeSignatureInSettingsMusicList()
    {
        string path = FindRepoFile(Path.Combine("Resources", "Raw", "about.html"));
        string text = File.ReadAllText(path);

        Assert.DoesNotContain(
            "<strong>Time signature</strong> — 2/2, 2/4, 3/4, 4/4, 5/4, 3/8, 6/8, 9/8, or 12/8.",
            text);
        Assert.Contains("Change the <strong>time signature</strong> on the <strong>Music</strong> page", text);
        Assert.Contains("tapping the", text);
        Assert.Contains("meter numerals", text);
    }

    [Fact]
    public void CountInBeatsPerMeasure_FollowsSelectedMeter()
    {
        foreach (string meter in TimeSignatureControlLogic.Options)
        {
            int beats = WaitingCountInLogic.GetBeatsPerMeasure(meter);
            Assert.True(beats > 0, meter);
        }
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            string nested = Path.Combine(dir.FullName, "musicmate", relativePath);
            if (File.Exists(nested))
                return nested;

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}

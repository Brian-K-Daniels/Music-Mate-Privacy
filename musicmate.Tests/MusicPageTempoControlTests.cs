using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Music-page tempo control: five nudge buttons with BPM-dependent steps, one authoritative
/// <see cref="NoteSessionService.Tempo"/>, and no Settings→Music tempo slider.
/// </summary>
[Collection("SessionPreferences")]
public class MusicPageTempoControlTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public MusicPageTempoControlTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose()
        => SessionPreferences.TestStore = null;

    [Theory]
    [InlineData(45, 10, 2)]
    [InlineData(59, 10, 2)]
    [InlineData(60, 10, 3)]
    [InlineData(71, 10, 3)]
    [InlineData(72, 20, 4)]
    [InlineData(100, 20, 4)]
    [InlineData(119, 20, 4)]
    [InlineData(120, 30, 6)]
    [InlineData(143, 30, 6)]
    [InlineData(144, 40, 8)]
    [InlineData(239, 40, 8)]
    [InlineData(240, 60, 12)]
    [InlineData(252, 60, 12)]
    public void GetStepSizes_MatchesBpmBands(int bpm, int outer, int inner)
    {
        var steps = TempoControlLogic.GetStepSizes(bpm);
        Assert.Equal(outer, steps.Outer);
        Assert.Equal(inner, steps.Inner);
    }

    [Theory]
    [InlineData(100, -20, 80)]
    [InlineData(100, -4, 96)]
    [InlineData(100, 4, 104)]
    [InlineData(100, 20, 120)]
    [InlineData(32, -10, NoteSessionService.MinTempo)]
    [InlineData(250, 60, NoteSessionService.MaxTempo)]
    [InlineData(30, -2, NoteSessionService.MinTempo)]
    [InlineData(252, 12, NoteSessionService.MaxTempo)]
    public void ApplyDelta_ClampsToAllowedRange(int current, int delta, int expected)
        => Assert.Equal(expected, TempoControlLogic.ApplyDelta(current, delta));

    [Fact]
    public void FormatButtonLabel_CenterShowsCurrentBpm()
    {
        Assert.Equal("60", TempoControlLogic.FormatButtonLabel(60, 0));
        Assert.Equal("−10", TempoControlLogic.FormatButtonLabel(60, -10));
        Assert.Equal("+3", TempoControlLogic.FormatButtonLabel(60, 3));
    }

    [Fact]
    public void GetDeltaChoices_RecalculatesWhenCrossingThreshold()
    {
        Assert.Equal(new[] { -20, -4, 0, 5, 20 }, TempoControlLogic.GetDeltaChoices(119));
        Assert.Equal(new[] { -30, -6, 0, 7, 30 }, TempoControlLogic.GetDeltaChoices(120));
    }

    [Theory]
    [InlineData(45, 2)]
    [InlineData(65, 3)]
    [InlineData(100, 4)]
    [InlineData(130, 6)]
    [InlineData(180, 8)]
    [InlineData(250, 12)]
    public void SmallSteps_DifferByOne(int bpm, int inner)
    {
        int[] deltas = TempoControlLogic.GetDeltaChoices(bpm);
        Assert.Equal(-inner, deltas[1]);
        Assert.Equal(inner + 1, deltas[3]);
        Assert.Equal(1, Math.Abs(deltas[3]) - Math.Abs(deltas[1]));
    }

    [Fact]
    public void EveryIntegerBpm_IsReachableFromEveryOtherUsingButtons()
    {
        int min = NoteSessionService.MinTempo;
        int max = NoteSessionService.MaxTempo;
        Assert.Equal(30, min);
        Assert.Equal(252, max);

        for (int start = min; start <= max; start++)
        {
            var seen = new bool[max - min + 1];
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start - min] = true;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int delta in TempoControlLogic.GetDeltaChoices(current))
                {
                    if (delta == 0)
                        continue;
                    int next = TempoControlLogic.ApplyDelta(current, delta);
                    if (next == current)
                        continue;
                    int index = next - min;
                    if (seen[index])
                        continue;
                    seen[index] = true;
                    queue.Enqueue(next);
                }
            }

            Assert.DoesNotContain(false, seen);
        }
    }

    [Fact]
    public void SettingTempo_UpdatesPreferenceAndConductorTimingBpm()
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            Tempo = 60,
            CooldownMs = 0,
            SampleRate = 44100,
            PitchWindowSize = 4096,
        };
        session.NotesToDraw.Add(new NoteInfo
        {
            Midi = 60,
            Name = "C4",
            TargetFreq = 261.6,
            DurationBeats = 1,
            Duration = NoteDuration.Quarter,
            StartBeat = 0,
            GateBeatsAfterPrevious = 0,
        });
        session.NotesToDraw.Add(new NoteInfo
        {
            Midi = 62,
            Name = "D4",
            TargetFreq = 293.7,
            DurationBeats = 1,
            Duration = NoteDuration.Quarter,
            StartBeat = 1,
            GateBeatsAfterPrevious = 1,
        });
        session.ConfigureRhythmStartGates();

        int next = TempoControlLogic.ApplyDelta(session.Tempo, TempoControlLogic.GetStepSizes(session.Tempo).Outer);
        session.Tempo = next;

        Assert.Equal(70, session.Tempo);
        Assert.Equal(70, session.MusicBpm);
        Assert.Equal(70, SessionPreferences.Get("musicmate.MusicBpm", 0));
        Assert.Equal(70, SessionPreferences.Get("musicmate.PlaybackBpm", 0));

        // Conductor / count-in math must follow the new BPM immediately.
        Assert.Equal(60000.0 / 70, ConductorOnsetTiming.MsPerBeat(70), precision: 6);
        double expectedOnset = session.GetConductorExpectedOnsetMsPublic(1);
        Assert.Equal(
            ConductorOnsetTiming.ExpectedOnsetMs(0, 1.0, 70),
            expectedOnset,
            precision: 3);
    }

    [Fact]
    public void CountInMsPerBeat_FollowsSessionTempoAfterNudge()
    {
        int bpm = TempoControlLogic.ApplyDelta(100, -20);
        Assert.Equal(80, bpm);
        Assert.Equal(60000.0 / 80, WaitingCountInLogic.MsPerBeat(80), precision: 6);
    }

    [Fact]
    public void SettingsPageXaml_NoLongerContainsTempoSliderPanel()
    {
        string path = FindRepoFile(Path.Combine("Pages", "SettingsPage.xaml"));
        string text = File.ReadAllText(path);

        Assert.DoesNotContain("Label Text=\"Tempo\"", text);
        Assert.DoesNotContain("Value=\"{Binding Tempo, Mode=TwoWay}\"", text);
        Assert.DoesNotContain("Minimum=\"{Binding MinTempo}\"", text);
    }

    [Fact]
    public void MusicPageXaml_HasFiveButtonTempoControl()
    {
        string path = FindRepoFile(Path.Combine("Pages", "MusicPage.xaml"));
        string text = File.ReadAllText(path);

        Assert.Contains("x:Name=\"TempoControlRow\"", text);
        Assert.Contains("OnTempoDeltaClicked", text);
        Assert.Contains("OnTempoMarkingHitTargetClicked", text);
        Assert.Contains("x:Name=\"TempoMarkingHitTarget\"", text);
        Assert.Contains("<Button x:Name=\"TempoMarkingHitTarget\"", text);
        Assert.Contains("StaffOverlayGrid", text);
        Assert.Contains("x:Name=\"TempoMinus10Button\"", text);
        Assert.Contains("x:Name=\"TempoMinus5Button\"", text);
        Assert.Contains("x:Name=\"TempoCurrentButton\"", text);
        Assert.Contains("x:Name=\"TempoPlus5Button\"", text);
        Assert.Contains("x:Name=\"TempoPlus10Button\"", text);
        Assert.Contains("VerticalOptions=\"Start\"", text);
    }

    [Fact]
    public void AboutHtml_DoesNotTellUsersToChangeTempoInSettings()
    {
        string path = FindRepoFile(Path.Combine("Resources", "Raw", "about.html"));
        string text = File.ReadAllText(path);

        Assert.DoesNotContain("change tempo in <strong>Settings</strong>", text);
        Assert.DoesNotContain("BPM</strong> slider sets practice", text);
        Assert.Contains("tapping the ♩ = BPM marking", text);
    }

    [Fact]
    public void FiveButtonLabels_MatchTempoControlUiAt100Bpm()
    {
        Assert.Equal(
            new[] { "−20", "−4", "100", "+5", "+20" },
            TempoControlLogic.GetDeltaChoices(100)
                .Select(d => TempoControlLogic.FormatButtonLabel(100, d))
                .ToArray());
    }

    [Fact]
    public void SessionTempoRange_Is30To252()
    {
        Assert.Equal(30, NoteSessionService.MinTempo);
        Assert.Equal(252, NoteSessionService.MaxTempo);
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

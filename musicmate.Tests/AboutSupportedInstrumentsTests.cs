using musicmate.Services;

namespace musicmate.Tests;

public class AboutSupportedInstrumentsTests
{
    [Fact]
    public void PackagedAboutHtml_ListsEveryDropdownInstrument()
    {
        string html = ReadAboutHtml();

        foreach (string name in InstrumentCatalog.DisplayNames)
        {
            Assert.Contains(name, html);
        }
    }

    [Fact]
    public void PackagedAboutHtml_KeepsGuitarAndBassGuitarInSupportedSection()
    {
        string html = ReadAboutHtml();
        int supported = html.IndexOf("Supported Instruments", StringComparison.Ordinal);
        int considered = html.IndexOf("Instruments Under Consideration", StringComparison.Ordinal);
        Assert.True(supported >= 0);
        Assert.True(considered > supported);

        string supportedSection = html[supported..considered];
        Assert.Contains("<strong>Guitar</strong>", supportedSection);
        Assert.Contains("<strong>Bass Guitar</strong>", supportedSection);
        Assert.Contains("sounds one octave lower than written", supportedSection);
        Assert.DoesNotContain("not in the Instrument list yet", supportedSection);
    }

    [Fact]
    public void PackagedAboutHtml_MarksConsideredInstrumentsAsUnavailable()
    {
        string html = ReadAboutHtml();
        int considered = html.IndexOf("Instruments Under Consideration", StringComparison.Ordinal);
        Assert.True(considered >= 0);
        string section = html[considered..];

        Assert.Contains("not</strong> in the Instrument list yet", section);
        Assert.Contains("Cornet", section);
        Assert.Contains("Viola", section);
        Assert.Contains("Violin", section);
        Assert.Contains("Use <strong>Bb Trumpet</strong>", section);
        Assert.DoesNotContain("these instruments are already available", section);
    }

    [Fact]
    public void PackagedAboutHtml_WelcomeRemainsTheLogicalStart()
    {
        string html = ReadAboutHtml();
        var lines = AboutLogicalLineMap.SplitSourceLines(html);
        int welcome = AboutLogicalLineMap.FindWelcomeLineIndex(lines);
        Assert.Contains("Welcome", lines[welcome], StringComparison.Ordinal);
        Assert.Equal(1, AboutLogicalLineMap.Analyze(html, "Welcome").MatchLines[0]);
    }

    [Fact]
    public void PackagedAboutHtml_DoesNotAdvertiseChordDetection()
    {
        string html = ReadAboutHtml();
        Assert.Contains("one note at a time", html);
        Assert.Contains("does not grade chords", html);
    }

    private static string ReadAboutHtml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Resources", "Raw", "about.html");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate Resources/Raw/about.html");
    }
}

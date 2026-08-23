using musicmate.Services;

namespace musicmate.Tests;

public class AboutLogicalLineMapTests
{
    private const string SampleHtml = """
        <!DOCTYPE html>
        <html>
        <head><title>Music Mate</title></head>
        <body>
        <h1>Music Mate</h1>
        <h2>Welcome</h2>
        <p>Alpha
            Beta Alpha</p>
        <p>Gamma</p>
        </body>
        </html>
        """;

    [Fact]
    public void Welcome_IsLogicalLineOne()
    {
        var lines = AboutLogicalLineMap.SplitSourceLines(SampleHtml);
        int welcome = AboutLogicalLineMap.FindWelcomeLineIndex(lines);
        Assert.StartsWith("Welcome", AboutLogicalLineMap.StripTags(lines[welcome]).TrimStart());
        Assert.Contains("Welcome", lines[welcome], StringComparison.Ordinal);
    }

    [Fact]
    public void TotalLines_CountsSourceLinesFromWelcomeToEnd()
    {
        var lines = AboutLogicalLineMap.SplitSourceLines(SampleHtml);
        int welcome = AboutLogicalLineMap.FindWelcomeLineIndex(lines);
        int expected = lines.Length - welcome;
        Assert.Equal(expected, AboutLogicalLineMap.CountLinesFromWelcome(SampleHtml));
        Assert.Equal(expected, AboutLogicalLineMap.Analyze(SampleHtml, null).TotalLines);
    }

    [Fact]
    public void SearchWelcome_HighlightsLineOne()
    {
        var result = AboutLogicalLineMap.Analyze(SampleHtml, "Welcome");
        Assert.NotEmpty(result.MatchLines);
        Assert.Equal(1, result.MatchLines[0]);
        Assert.Equal("1/" + result.TotalLines, AboutLogicalLineMap.FormatIndicator(
            true, 0, result.MatchLines, result.TotalLines));
    }

    [Fact]
    public void TwoMatchesOnSameSourceLine_ShareTheSameM()
    {
        const string html = """
            <body>
            <h2>Welcome</h2>
            <p>foo foo</p>
            </body>
            """;

        var result = AboutLogicalLineMap.Analyze(html, "foo");
        Assert.Equal(2, result.MatchLines.Length);
        Assert.Equal(result.MatchLines[0], result.MatchLines[1]);
        Assert.True(result.MatchLines[0] >= 1);
    }

    [Fact]
    public void MatchesOnWrappedSourceLines_GetDifferentLineNumbers()
    {
        var result = AboutLogicalLineMap.Analyze(SampleHtml, "Alpha");
        Assert.Equal(2, result.MatchLines.Length);
        Assert.NotEqual(result.MatchLines[0], result.MatchLines[1]);
        Assert.Equal(result.MatchLines[0] + 1, result.MatchLines[1]);
    }

    [Fact]
    public void NoMatches_FormatIsEmpty()
    {
        var result = AboutLogicalLineMap.Analyze(SampleHtml, "ZzNotInAbout");
        Assert.Empty(result.MatchLines);
        Assert.Equal(string.Empty, AboutLogicalLineMap.FormatIndicator(
            false, -1, result.MatchLines, result.TotalLines));
        Assert.Equal(string.Empty, AboutLogicalLineMap.FormatIndicator(
            true, 0, result.MatchLines, result.TotalLines));
    }

    [Fact]
    public void ClearSearch_FormatIsEmpty()
    {
        var result = AboutLogicalLineMap.Analyze(SampleHtml, "");
        Assert.Empty(result.MatchLines);
        Assert.True(result.TotalLines > 0);
        Assert.Equal(string.Empty, AboutLogicalLineMap.FormatIndicator(
            false, -1, result.MatchLines, result.TotalLines));
    }

    [Fact]
    public void NextIndex_UsesSameMatchLineArray()
    {
        var result = AboutLogicalLineMap.Analyze(SampleHtml, "Alpha");
        Assert.Equal(2, result.MatchLines.Length);
        string first = AboutLogicalLineMap.FormatIndicator(true, 0, result.MatchLines, result.TotalLines);
        string second = AboutLogicalLineMap.FormatIndicator(true, 1, result.MatchLines, result.TotalLines);
        Assert.NotEqual(first, second);
        Assert.EndsWith("/" + result.TotalLines, first);
        Assert.EndsWith("/" + result.TotalLines, second);
    }

    [Fact]
    public void PackagedAboutHtml_WelcomeIsLineOne_AndTotalIsNotHardCoded()
    {
        string path = FindAboutHtml();
        string html = File.ReadAllText(path);
        var lines = AboutLogicalLineMap.SplitSourceLines(html);
        int welcome = AboutLogicalLineMap.FindWelcomeLineIndex(lines);
        Assert.Contains("Welcome", lines[welcome], StringComparison.Ordinal);
        Assert.Equal(lines.Length - welcome, AboutLogicalLineMap.CountLinesFromWelcome(html));
        Assert.True(AboutLogicalLineMap.CountLinesFromWelcome(html) > 10);

        var welcomeHits = AboutLogicalLineMap.Analyze(html, "Welcome");
        Assert.Equal(1, welcomeHits.MatchLines[0]);
    }

    private static string FindAboutHtml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Resources", "Raw", "about.html");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate Resources/Raw/about.html");
    }
}

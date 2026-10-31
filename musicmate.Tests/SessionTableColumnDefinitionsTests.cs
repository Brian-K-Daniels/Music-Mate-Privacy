using musicmate.Services;

namespace musicmate.Tests;

public class SessionTableColumnDefinitionsTests
{
    public static IEnumerable<object[]> ColumnKeys =>
        SessionTableColumnDefinitions.All.Select(d => new object[] { d.ColumnKey, d.Heading });

    [Fact]
    public void All_ContainsTwentySessionTableHeadings()
    {
        Assert.Equal(20, SessionTableColumnDefinitions.All.Count);
    }

    [Theory]
    [MemberData(nameof(ColumnKeys))]
    public void EveryDefinition_HasNonEmptyHeadingAndDefinition(string columnKey, string heading)
    {
        var definition = SessionTableColumnDefinitions.GetByColumnKey(columnKey);

        Assert.NotNull(definition);
        Assert.Equal(heading, definition!.Heading);
        Assert.False(string.IsNullOrWhiteSpace(definition.Definition));
    }

    [Fact]
    public void GetByColumnKey_ReturnsNullForUnknownKey()
    {
        Assert.Null(SessionTableColumnDefinitions.GetByColumnKey("NotARealColumn"));
    }

    [Fact]
    public void All_HeadingsMatchSessionTableAbbreviations()
    {
        var headings = SessionTableColumnDefinitions.All.Select(d => d.Heading).ToArray();

        Assert.Equal(
            [
                "Date", "Level", "Pc%", "Tmg", "Ovrl",
                "P+", "P−", "T+", "T−", "O+", "O−", "R+", "R−",
                "Key", "What", "Rand", "Acc%", "Hi", "Lo", "Inst"
            ],
            headings);
    }
}

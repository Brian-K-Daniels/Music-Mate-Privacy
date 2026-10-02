namespace musicmate.ViewModels;

using Microsoft.Maui.Controls;
using musicmate.Services;

public sealed class SessionColumnDefinitionDisplayRow
{
    public SessionColumnDefinitionDisplayRow(SessionTableColumnDefinition definition, bool isHighlighted)
    {
        ColumnKey = definition.ColumnKey;
        Heading = definition.Heading;
        Definition = definition.Definition;
        IsHighlighted = isHighlighted;
    }

    public string ColumnKey { get; }
    public string Heading { get; }
    public string Definition { get; }
    public bool IsHighlighted { get; }

    public string HeadingDisplay => IsHighlighted ? $"\u25b6 {Heading}" : Heading;

    public FontAttributes HeadingFontAttributes =>
        IsHighlighted ? FontAttributes.Bold : FontAttributes.None;
}

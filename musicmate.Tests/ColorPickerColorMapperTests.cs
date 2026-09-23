using Microsoft.Maui.Graphics;
using musicmate.Controls;
using musicmate.Services;

namespace musicmate.Tests;

public class ColorPickerColorMapperTests
{
    [Fact]
    public void MixWithWhiteness_AtZero_ReturnsBaseColor()
    {
        var baseColor = Color.FromArgb("#8B4513");
        var mixed = ColorPickerColorMapper.MixWithWhiteness(baseColor, 0);
        Assert.Equal(baseColor.Red, mixed.Red, 3);
        Assert.Equal(baseColor.Green, mixed.Green, 3);
        Assert.Equal(baseColor.Blue, mixed.Blue, 3);
    }

    [Fact]
    public void MixWithWhiteness_AtOne_ReturnsWhite()
    {
        var baseColor = Color.FromArgb("#8B4513");
        var mixed = ColorPickerColorMapper.MixWithWhiteness(baseColor, 1);
        Assert.Equal(1f, mixed.Red, 3);
        Assert.Equal(1f, mixed.Green, 3);
        Assert.Equal(1f, mixed.Blue, 3);
    }

    [Fact]
    public void Decompose_RoundTrips_MixedColor()
    {
        var baseColor = Color.FromArgb("#8B4513");
        const double whiteness = 0.35;
        var target = ColorPickerColorMapper.MixWithWhiteness(baseColor, whiteness);

        var (decomposedBase, decomposedW) = ColorPickerColorMapper.Decompose(target);
        var roundTrip = ColorPickerColorMapper.MixWithWhiteness(decomposedBase, decomposedW);

        Assert.True(ColorPickerColorMapper.ColorDistance(target, roundTrip) < 0.002);
    }

    [Fact]
    public void Decompose_White_ReturnsFullWhiteness()
    {
        var (baseColor, whiteness) = ColorPickerColorMapper.Decompose(Colors.White);
        Assert.True(whiteness >= 0.99);
        var mixed = ColorPickerColorMapper.MixWithWhiteness(baseColor, whiteness);
        Assert.True(ColorPickerColorMapper.ColorDistance(Colors.White, mixed) < 0.01);
    }
}

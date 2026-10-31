using musicmate.Drawables;

namespace musicmate.Tests;

/// <summary>
/// A sixteenth that shares a primary beam with an eighth must still get a sixteenth hook.
/// Without it the note is drawn as an eighth, so the ink between two bar lines
/// adds up to more than the measure.
/// </summary>
public class SixteenthBeamSpanTests
{
    [Fact]
    public void EighthThenSixteenth_SixteenthGetsHook()
    {
        bool ok = StaffDrawable.TryResolveSecondaryBeamSpan(
            segStart: 1,
            segEnd: 1,
            noteLeft: 40f,
            noteRight: 40f,
            groupLeft: 10f,
            groupRight: 40f,
            groupCount: 2,
            drawLeft: 10f,
            drawRight: 40f,
            minHook: 4f,
            out float left,
            out float right);

        Assert.True(ok);
        Assert.True(right - left >= 4f, $"hook width {right - left}");
        Assert.True(left < 40f);
        Assert.Equal(40f, right);
    }

    [Fact]
    public void SixteenthThenEighth_SixteenthGetsHook()
    {
        bool ok = StaffDrawable.TryResolveSecondaryBeamSpan(
            segStart: 0,
            segEnd: 0,
            noteLeft: 10f,
            noteRight: 10f,
            groupLeft: 10f,
            groupRight: 40f,
            groupCount: 2,
            drawLeft: 10f,
            drawRight: 40f,
            minHook: 4f,
            out float left,
            out float right);

        Assert.True(ok);
        Assert.True(right - left >= 4f, $"hook width {right - left}");
        Assert.Equal(10f, left);
        Assert.True(right > 10f);
    }

    [Fact]
    public void TwoAdjacentSixteenths_SpanBetweenThem()
    {
        bool ok = StaffDrawable.TryResolveSecondaryBeamSpan(
            segStart: 0,
            segEnd: 1,
            noteLeft: 10f,
            noteRight: 28f,
            groupLeft: 10f,
            groupRight: 40f,
            groupCount: 3,
            drawLeft: 10f,
            drawRight: 40f,
            minHook: 4f,
            out float left,
            out float right);

        Assert.True(ok);
        Assert.Equal(10f, left);
        Assert.Equal(28f, right);
    }
}

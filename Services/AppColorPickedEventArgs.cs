using Microsoft.Maui.Graphics;

namespace musicmate.Services;

public sealed class AppColorPickedEventArgs : EventArgs
{
    public AppColorPickedEventArgs(AppColorTarget target, Color color)
    {
        Target = target;
        Color = color;
    }

    public AppColorTarget Target { get; }
    public Color Color { get; }
}

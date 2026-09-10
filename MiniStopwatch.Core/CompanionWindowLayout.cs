namespace MiniStopwatch.Core;

public readonly record struct LayoutRect(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public static class CompanionWindowLayout
{
    public static LayoutRect ResolveStatsWindow(
        LayoutRect tracker,
        LayoutRect workArea,
        double statsWidth,
        double statsHeight,
        double gap)
    {
        var left = Clamp(
            tracker.Left + (tracker.Width - statsWidth) / 2,
            workArea.Left,
            workArea.Right - statsWidth);
        var below = tracker.Bottom + gap;
        var above = tracker.Top - statsHeight - gap;
        var top = below + statsHeight <= workArea.Bottom
            ? below
            : Math.Max(workArea.Top, above);
        return new LayoutRect(left, top, statsWidth, statsHeight);
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Clamp(value, minimum, Math.Max(minimum, maximum));
}

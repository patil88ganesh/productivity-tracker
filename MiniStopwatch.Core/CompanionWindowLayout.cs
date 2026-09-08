namespace MiniStopwatch.Core;

public readonly record struct LayoutRect(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public bool Intersects(LayoutRect other) =>
        Left < other.Right &&
        Right > other.Left &&
        Top < other.Bottom &&
        Bottom > other.Top;
}

public static class CompanionWindowLayout
{
    public static LayoutRect ResolvePlaybackButton(
        LayoutRect tracker,
        LayoutRect workArea,
        double buttonWidth,
        double buttonHeight,
        double surfaceInset,
        double gap)
    {
        var alignedLeft = Clamp(
            tracker.Right - buttonWidth + surfaceInset,
            workArea.Left,
            workArea.Right - buttonWidth);
        var above = tracker.Top - buttonHeight + surfaceInset - gap;
        if (above >= workArea.Top)
        {
            return new LayoutRect(alignedLeft, above, buttonWidth, buttonHeight);
        }

        var below = tracker.Bottom + gap - surfaceInset;
        if (below + buttonHeight <= workArea.Bottom)
        {
            return new LayoutRect(alignedLeft, below, buttonWidth, buttonHeight);
        }

        var sideTop = Clamp(
            tracker.Top - surfaceInset,
            workArea.Top,
            workArea.Bottom - buttonHeight);
        var right = tracker.Right + gap - surfaceInset;
        if (right + buttonWidth <= workArea.Right)
        {
            return new LayoutRect(right, sideTop, buttonWidth, buttonHeight);
        }

        var left = tracker.Left - buttonWidth - gap + surfaceInset;
        if (left >= workArea.Left)
        {
            return new LayoutRect(left, sideTop, buttonWidth, buttonHeight);
        }

        return new LayoutRect(alignedLeft, sideTop, buttonWidth, buttonHeight);
    }

    public static LayoutRect ResolveStatsWindow(
        LayoutRect tracker,
        LayoutRect workArea,
        double statsWidth,
        double statsHeight,
        double gap,
        LayoutRect playback,
        bool playbackVisible)
    {
        var left = Clamp(
            tracker.Left + (tracker.Width - statsWidth) / 2,
            workArea.Left,
            workArea.Right - statsWidth);
        var below = tracker.Bottom + gap;
        var above = tracker.Top - statsHeight - gap;

        if (playbackVisible &&
            HorizontallyOverlaps(left, statsWidth, playback))
        {
            if (playback.Top >= tracker.Top)
            {
                below = Math.Max(below, playback.Bottom + gap);
            }
            else
            {
                above = Math.Min(above, playback.Top - statsHeight - gap);
            }
        }

        var top = below + statsHeight <= workArea.Bottom
            ? below
            : Math.Max(workArea.Top, above);
        var stats = new LayoutRect(left, top, statsWidth, statsHeight);
        if (!playbackVisible || !stats.Intersects(playback))
        {
            return stats;
        }

        var leftOfPlayback = playback.Left - statsWidth - gap;
        if (leftOfPlayback >= workArea.Left)
        {
            return stats with { Left = leftOfPlayback };
        }

        var rightOfPlayback = playback.Right + gap;
        if (rightOfPlayback + statsWidth <= workArea.Right)
        {
            return stats with { Left = rightOfPlayback };
        }

        var belowPlayback = playback.Bottom + gap;
        if (belowPlayback + statsHeight <= workArea.Bottom)
        {
            return stats with { Top = belowPlayback };
        }

        var abovePlayback = playback.Top - statsHeight - gap;
        return abovePlayback >= workArea.Top
            ? stats with { Top = abovePlayback }
            : stats;
    }

    private static bool HorizontallyOverlaps(
        double left,
        double width,
        LayoutRect other) =>
        left < other.Right && left + width > other.Left;

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Clamp(value, minimum, Math.Max(minimum, maximum));
}

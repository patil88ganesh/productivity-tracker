namespace MiniStopwatch.Core;

public enum ResizeRegion
{
    Client = 1,
    Left = 10,
    Right = 11,
    Top = 12,
    TopLeft = 13,
    TopRight = 14,
    Bottom = 15,
    BottomLeft = 16,
    BottomRight = 17,
}

public static class ResizeRegionResolver
{
    public static ResizeRegion Resolve(
        double x,
        double y,
        double width,
        double height,
        double borderThickness)
    {
        if (width <= 0 || height <= 0 || borderThickness <= 0)
        {
            return ResizeRegion.Client;
        }

        var onLeft = x >= 0 && x < borderThickness;
        var onRight = x <= width && x > width - borderThickness;
        var onTop = y >= 0 && y < borderThickness;
        var onBottom = y <= height && y > height - borderThickness;

        if (onTop && onLeft)
        {
            return ResizeRegion.TopLeft;
        }

        if (onTop && onRight)
        {
            return ResizeRegion.TopRight;
        }

        if (onBottom && onLeft)
        {
            return ResizeRegion.BottomLeft;
        }

        if (onBottom && onRight)
        {
            return ResizeRegion.BottomRight;
        }

        if (onLeft)
        {
            return ResizeRegion.Left;
        }

        if (onRight)
        {
            return ResizeRegion.Right;
        }

        if (onTop)
        {
            return ResizeRegion.Top;
        }

        return onBottom ? ResizeRegion.Bottom : ResizeRegion.Client;
    }
}

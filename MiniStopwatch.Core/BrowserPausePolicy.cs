namespace MiniStopwatch.Core;

public enum BrowserActivityKind
{
    None,
    YouTube,
    OtherDistracting,
    UnknownDistracting,
}

public static class BrowserPausePolicy
{
    public static BrowserActivityKind ResolveActivity(
        BrowserActivityKind activity,
        bool continueOnYouTube,
        bool isForegroundYouTubeWindow)
    {
        return continueOnYouTube &&
            activity == BrowserActivityKind.UnknownDistracting &&
            isForegroundYouTubeWindow
                ? BrowserActivityKind.YouTube
                : activity;
    }

    public static bool IsForegroundYouTubeWindow(
        string? processName,
        string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(processName) ||
            string.IsNullOrWhiteSpace(windowTitle) ||
            !string.Equals(
                processName,
                "msedge",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                processName,
                "chrome",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return windowTitle.Contains(
            " - YouTube",
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldPause(
        bool pauseOnSelectedWebsites,
        bool continueOnYouTube,
        BrowserActivityKind activity)
    {
        if (!pauseOnSelectedWebsites ||
            activity == BrowserActivityKind.None)
        {
            return false;
        }

        if (!continueOnYouTube)
        {
            return true;
        }

        return activity is
            BrowserActivityKind.OtherDistracting or
            BrowserActivityKind.UnknownDistracting;
    }
}

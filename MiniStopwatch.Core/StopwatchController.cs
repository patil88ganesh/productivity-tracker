namespace MiniStopwatch.Core;

public enum AutomaticPauseReason
{
    SessionLocked,
    DistractingWebsite,
}

public sealed class StopwatchController
{
    private enum ContinueCountingHandoffState
    {
        None,
        AwaitingFocusLoss,
        AwaitingRefocus,
        Confirmed,
    }

    private static readonly TimeSpan ContinueCountingHandoffWindow =
        TimeSpan.FromSeconds(30);

    private readonly IMonotonicClock clock;
    private readonly HashSet<AutomaticPauseReason> automaticPauseReasons = [];
    private readonly HashSet<AutomaticPauseReason> ignoredAutomaticPauseReasons = [];
    private TimeSpan accumulated;
    private TimeSpan startedAt;
    private TimeSpan? continueCountingAvailableUntil;
    private string? continueCountingOfferVisitToken;
    private string? continueCountingVisitToken;
    private ContinueCountingHandoffState continueCountingHandoffState;
    private bool distractingWebsiteIsActive;
    private string? distractingWebsiteVisitToken;
    private bool resumeAfterAutomaticPause;

    public StopwatchController(IMonotonicClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool IsRunning { get; private set; }

    public bool IsAutomaticallyPaused =>
        resumeAfterAutomaticPause && automaticPauseReasons.Count > 0;

    public bool CanContinueCountingOnDistractingWebsite =>
        !ignoredAutomaticPauseReasons.Contains(
            AutomaticPauseReason.DistractingWebsite) &&
        !automaticPauseReasons.Contains(AutomaticPauseReason.SessionLocked) &&
        ((resumeAfterAutomaticPause &&
          automaticPauseReasons.Contains(AutomaticPauseReason.DistractingWebsite) &&
          distractingWebsiteVisitToken != null) ||
         (continueCountingAvailableUntil.HasValue &&
          clock.Now <= continueCountingAvailableUntil.Value &&
          continueCountingOfferVisitToken != null));

    public bool IsContinuingCountingOnDistractingWebsite =>
        ignoredAutomaticPauseReasons.Contains(
            AutomaticPauseReason.DistractingWebsite) &&
        continueCountingHandoffState is
            ContinueCountingHandoffState.AwaitingFocusLoss or
            ContinueCountingHandoffState.Confirmed;

    public bool HasContinueCountingOverride =>
        ignoredAutomaticPauseReasons.Contains(
            AutomaticPauseReason.DistractingWebsite);

    public TimeSpan Elapsed =>
        IsRunning ? accumulated + (clock.Now - startedAt) : accumulated;

    public void Toggle()
    {
        if (IsRunning || resumeAfterAutomaticPause)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    public void Start()
    {
        if (IsRunning || resumeAfterAutomaticPause)
        {
            return;
        }

        if (automaticPauseReasons.Count > 0)
        {
            resumeAfterAutomaticPause = true;
            return;
        }

        startedAt = clock.Now;
        IsRunning = true;
    }

    public void Stop()
    {
        if (IsRunning)
        {
            accumulated = Elapsed;
        }

        IsRunning = false;
        resumeAfterAutomaticPause = false;
        if (!ignoredAutomaticPauseReasons.Contains(
            AutomaticPauseReason.DistractingWebsite))
        {
            continueCountingAvailableUntil = null;
        }
    }

    public void Reset()
    {
        accumulated = TimeSpan.Zero;
        if (IsRunning)
        {
            startedAt = clock.Now;
        }
    }

    public void Add(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Added time cannot be negative.");
        }

        if (IsRunning)
        {
            accumulated = Elapsed + duration;
            startedAt = clock.Now;
            return;
        }

        accumulated += duration;
    }

    public void OnSessionLocked()
    {
        SetAutomaticPause(AutomaticPauseReason.SessionLocked, isActive: true);
    }

    public void OnSessionUnlocked()
    {
        SetAutomaticPause(AutomaticPauseReason.SessionLocked, isActive: false);
    }

    public void OnDistractingWebsiteChanged(
        bool isActive,
        string? visitToken = null)
    {
        visitToken = string.IsNullOrWhiteSpace(visitToken) ? null : visitToken;
        distractingWebsiteIsActive = isActive;
        distractingWebsiteVisitToken = visitToken;
        if (!HasContinueCountingOverride &&
            continueCountingOfferVisitToken != null &&
            !string.Equals(
                continueCountingOfferVisitToken,
                visitToken,
                StringComparison.Ordinal))
        {
            continueCountingAvailableUntil = null;
            continueCountingOfferVisitToken = null;
        }
        if (HasContinueCountingOverride &&
            !string.Equals(
                continueCountingVisitToken,
                visitToken,
                StringComparison.Ordinal))
        {
            ClearContinueCountingOverride();
        }

        SetAutomaticPause(AutomaticPauseReason.DistractingWebsite, isActive);
    }

    public void ContinueCountingOnDistractingWebsite()
    {
        ContinueCountingThrough(AutomaticPauseReason.DistractingWebsite);
    }

    public void CancelContinueCountingOnDistractingWebsite()
    {
        ClearContinueCountingOverride();
        if (distractingWebsiteIsActive)
        {
            SetAutomaticPause(
                AutomaticPauseReason.DistractingWebsite,
                isActive: true);
        }
    }

    private void SetAutomaticPause(AutomaticPauseReason reason, bool isActive)
    {
        if (isActive)
        {
            if (ignoredAutomaticPauseReasons.Contains(reason))
            {
                if (reason != AutomaticPauseReason.DistractingWebsite)
                {
                    return;
                }

                if (continueCountingHandoffState is
                    ContinueCountingHandoffState.AwaitingFocusLoss or
                    ContinueCountingHandoffState.Confirmed)
                {
                    return;
                }

                if (continueCountingHandoffState ==
                        ContinueCountingHandoffState.AwaitingRefocus &&
                    continueCountingAvailableUntil.HasValue &&
                    clock.Now <= continueCountingAvailableUntil.Value)
                {
                    continueCountingHandoffState =
                        ContinueCountingHandoffState.Confirmed;
                    continueCountingAvailableUntil = null;
                    return;
                }

                ignoredAutomaticPauseReasons.Remove(reason);
                continueCountingHandoffState =
                    ContinueCountingHandoffState.None;
                continueCountingVisitToken = null;
            }

            if (!automaticPauseReasons.Add(reason))
            {
                return;
            }

            if (IsRunning)
            {
                accumulated = Elapsed;
                IsRunning = false;
                resumeAfterAutomaticPause = true;
            }

            return;
        }

        if (reason == AutomaticPauseReason.DistractingWebsite &&
            automaticPauseReasons.Contains(reason) &&
            automaticPauseReasons.Count == 1 &&
            resumeAfterAutomaticPause &&
            distractingWebsiteVisitToken != null)
        {
            continueCountingAvailableUntil =
                clock.Now + ContinueCountingHandoffWindow;
            continueCountingOfferVisitToken = distractingWebsiteVisitToken;
        }

        if (ignoredAutomaticPauseReasons.Contains(reason) &&
            reason == AutomaticPauseReason.DistractingWebsite)
        {
            if (continueCountingHandoffState ==
                    ContinueCountingHandoffState.AwaitingFocusLoss ||
                continueCountingHandoffState ==
                    ContinueCountingHandoffState.Confirmed)
            {
                continueCountingHandoffState =
                    ContinueCountingHandoffState.AwaitingRefocus;
                continueCountingAvailableUntil =
                    clock.Now + ContinueCountingHandoffWindow;
            }
            else if (continueCountingHandoffState ==
                          ContinueCountingHandoffState.AwaitingRefocus &&
                      continueCountingAvailableUntil.HasValue &&
                      clock.Now > continueCountingAvailableUntil.Value)
            {
                ignoredAutomaticPauseReasons.Remove(reason);
                continueCountingHandoffState =
                    ContinueCountingHandoffState.None;
                continueCountingAvailableUntil = null;
                continueCountingVisitToken = null;
            }
        }

        if (!automaticPauseReasons.Remove(reason) ||
            automaticPauseReasons.Count > 0 ||
            !resumeAfterAutomaticPause)
        {
            return;
        }

        startedAt = clock.Now;
        IsRunning = true;
        resumeAfterAutomaticPause = false;
    }

    private void ContinueCountingThrough(AutomaticPauseReason reason)
    {
        var automaticPauseWasActive = automaticPauseReasons.Contains(reason);
        var visitToken = automaticPauseWasActive
            ? distractingWebsiteVisitToken
            : continueCountingOfferVisitToken;
        if (automaticPauseReasons.Contains(AutomaticPauseReason.SessionLocked))
        {
            return;
        }

        if (reason == AutomaticPauseReason.DistractingWebsite &&
            visitToken == null)
        {
            return;
        }

        if ((!resumeAfterAutomaticPause || !automaticPauseWasActive) &&
            (!continueCountingAvailableUntil.HasValue ||
             clock.Now > continueCountingAvailableUntil.Value))
        {
            return;
        }

        automaticPauseReasons.Remove(reason);
        ignoredAutomaticPauseReasons.Add(reason);
        continueCountingVisitToken = visitToken;
        continueCountingHandoffState = automaticPauseWasActive
            ? ContinueCountingHandoffState.AwaitingFocusLoss
            : ContinueCountingHandoffState.AwaitingRefocus;
        continueCountingAvailableUntil =
            clock.Now + ContinueCountingHandoffWindow;
        if (automaticPauseReasons.Count > 0)
        {
            return;
        }

        if (resumeAfterAutomaticPause)
        {
            startedAt = clock.Now;
            IsRunning = true;
            resumeAfterAutomaticPause = false;
        }
    }

    private void ClearContinueCountingOverride()
    {
        ignoredAutomaticPauseReasons.Remove(
            AutomaticPauseReason.DistractingWebsite);
        continueCountingHandoffState = ContinueCountingHandoffState.None;
        continueCountingAvailableUntil = null;
        continueCountingOfferVisitToken = null;
        continueCountingVisitToken = null;
    }
}

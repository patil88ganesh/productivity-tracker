# Code Review — Round 3 of 6 (sequential mode)

## Review Summary
- **Round**: 3
- **Theme**: Edge cases & robustness
- **Mode**: sequential
- **Model**: Claude Opus 5 (slot 3, latest Anthropic Opus, highest reasoning setting)
- **Artifact**: C:\Users\gapat.FAREAST\MiniStopwatch\reviews\task-290-attempt-1-review-3-claude-opus-5.md
- **Issues Found**: 5
- **Verdict**: ISSUES_FOUND

## Evidence Checklist
- [x] Enumerated the change set with `git --no-pager status` and `git --no-pager diff --stat` in `C:\Users\gapat.FAREAST\MiniStopwatch` (10 modified files, 2 untracked new files: `MiniStopwatch.App/PlaybackButtonWindow.xaml{,.cs}`).
- [x] Read the complete new Windows control: `MiniStopwatch.App/PlaybackButtonWindow.xaml` and `MiniStopwatch.App/PlaybackButtonWindow.xaml.cs` (all 190 lines, including the `WM_MOUSEACTIVATE` hook and `WS_EX_NOACTIVATE` application).
- [x] Read the full Windows host integration in `MiniStopwatch.App/MainWindow.xaml.cs` — constructor wiring (L83–88), `Window_StateChanged` (L325–341), `Window_SizeChanged`/`Window_LocationChanged` (L344–354), `RefreshDisplay` (L428–432), `SetOpacity` (L533), `ShowPlaybackButton` (L614–633), `PositionPlaybackButton` (L635–659), `GetCurrentMonitorWorkArea` (L835–859) — plus `MainWindow.xaml` (`WindowStyle="None"`, `ResizeMode="CanResize"`, `Topmost="True"`, `ShowInTaskbar="False"`).
- [x] Read the full macOS counterpart in `macos/Sources/TimerWindow.swift` — `PlaybackButtonPanel`/`PlaybackButtonView`/`PlaybackButtonController` (L180–362), child-window attach in `init()` (L436–443), `prepareForTermination`/`deinit` (L484–487, L519–523), miniaturize/deminiaturize hooks (L538–547), `refreshDisplay` (L786–791), `applyOpacity` (L916–919), `showPlaybackButton`/`positionPlaybackButton` (L999–1038).
- [x] Traced the automatic-pause state machine end to end in `MiniStopwatch.Core/StopwatchController.cs` (L24–25, L42–56, L118–140) and `macos/Sources/TimerEngine.swift` (L22–23, L94–112, L125–134) to validate the new `controlEnabled = !isAutomaticallyPaused` rule.
- [x] Compared the new non-activating-window plumbing against the already-shipped `MiniStopwatch.App/StatsWindow.xaml.cs` (identical `MA_NOACTIVATE` hook + `WS_EX_NOACTIVATE` pattern) — confirmed the new code follows the proven pattern, no new finding there.
- [x] Verified the publish target is 64-bit only (`build.ps1`: `dotnet publish ... -r win-x64`), which rules out an `EntryPointNotFoundException` for the `GetWindowLongPtrW`/`SetWindowLongPtrW` P/Invokes (those exports do not exist in 32-bit `user32.dll`). No finding raised.
- [x] Built the solution: `dotnet build MiniStopwatch.sln -c Release` → **Build succeeded, 0 Warning(s), 0 Error(s)** (confirms `PlaybackButtonWindow.xaml` is picked up by the default WPF globs).
- [x] Ran the test project: `dotnet run --project MiniStopwatch.Tests -c Release --no-build` → **All 25 tests passed**, including `Manual stop during automatic pause prevents resume`, which is the exact transition Issue 1 shows the new button cannot reach.
- [x] Checked the docs changes (`docs/index.html`, `docs/styles.css`) — `.context-playback-button` is `position: absolute` inside `.context-timer`, which is itself `position: absolute` (styles.css L835–849), so the new absolutely-positioned mock button has a correct containing block and is out of the parent's flex flow. No finding.
- [ ] Did not run the macOS build (`macos/build-package.sh` / `.github/workflows/build-macos.yml`) — no macOS host or Swift toolchain is available in this Windows review environment. macOS findings are derived from source reading and from the parity contract with the Windows implementation.

## Issues

### Issue 1: Play/Pause button arms an automatic-resume it can never cancel (both platforms)
- **Severity**: Medium
- **File**: `MiniStopwatch.App/PlaybackButtonWindow.xaml.cs`, `macos/Sources/TimerWindow.swift`
- **Line(s)**: `PlaybackButtonWindow.xaml.cs` L49 (`controlEnabled = !isAutomaticallyPaused;`) and L149–152; `TimerWindow.swift` L251 and L225–227. Supporting state machine: `MiniStopwatch.Core/StopwatchController.cs` L24–25, L42–56; `macos/Sources/TimerEngine.swift` L22–23, L125–134.
- **Description**: The new control gates itself on `IsAutomaticallyPaused`, but that flag is **not** "an automatic pause reason is active" — it is `resumeAfterAutomaticPause && automaticPauseReasons.Count > 0`, i.e. it is only true when the stopwatch was *running* at the moment the reason activated. When a pause reason activates while the timer is already manually paused, `SetAutomaticPause` returns early without setting `resumeAfterAutomaticPause` (`StopwatchController.cs` L120–129), so `IsAutomaticallyPaused` stays `false` and the button stays enabled and green.

  Reproduction (Windows, verified by code trace):
  1. Enable Focus Protection, leave the timer paused (any elapsed value, including `00:00:00`).
  2. The browser extension reports a distracting site → `OnDistractingWebsiteChanged(true)` → `automaticPauseReasons = { DistractingWebsite }`, `resumeAfterAutomaticPause == false`, `IsAutomaticallyPaused == false`.
  3. `RefreshDisplay` (`MainWindow.xaml.cs` L428) passes `isAutomaticallyPaused: false` → `controlEnabled = true`. The button is fully enabled and shows the green Play glyph.
  4. The user clicks it → `ToggleTracking()` → `StopwatchController.Toggle()` → `Start()` → the `automaticPauseReasons.Count > 0` branch at L49–53 sets `resumeAfterAutomaticPause = true` and returns. **The timer does not start.**
  5. The next `RefreshDisplay` now sees `IsAutomaticallyPaused == true` → `controlEnabled = false`. The button greys out and `ButtonSurface_MouseLeftButtonDown` returns at L149–152 for every subsequent click.

  The only way out of the armed state is the context menu ("Remain Paused" → `Toggle()` → `Stop()`), which is exactly the transition the existing passing test `Manual stop during automatic pause prevents resume` covers. `macos/Sources/TimerEngine.swift` has byte-for-byte equivalent semantics (L22–23, L125–134), so the same trap exists there; on macOS it is additionally reachable via `sessionDidResignActiveNotification` during fast user switching, where the screen is not locked.
- **Risk**: The button is a one-way door precisely in the scenario the feature advertises. A click that the user expects to start the timer silently does nothing, then permanently disables the control, and the user has no way to undo it from the control they were told to use. `README.md` (added in this diff) claims "It is disabled during automatic lock or Focus Protection pauses" — the button is in fact *enabled* throughout the automatic pause and only disables itself *after* the user has irreversibly armed a deferred resume, so the shipped behaviour contradicts the shipped documentation.
- **Suggested Fix**: Stop deriving the button's enabled state from `IsAutomaticallyPaused`. Either (a) expose a distinct predicate for "an automatic pause reason is active" (e.g. `HasAutomaticPauseReason => automaticPauseReasons.Count > 0` / `!automaticPauseReasons.isEmpty`) and gate `controlEnabled` on that, so the button is genuinely disabled for the whole automatic-pause window; or (b) keep the button enabled while `IsAutomaticallyPaused` is true so that a second click reaches `Toggle()`/`Stop()` and cancels the armed resume, matching the context-menu behaviour. Option (a) matches the README text; whichever is chosen, the Windows and macOS paths and the README must be brought into agreement, and a unit test should assert that clicking the control while a pause reason is active is either a no-op or reversible.

### Issue 2: On Windows the button is hidden forever if the tracker restores to `Maximized` instead of `Normal`
- **Severity**: Medium
- **File**: `MiniStopwatch.App/MainWindow.xaml.cs`
- **Line(s)**: L325–341 (`Window_StateChanged`), L614–620 (`ShowPlaybackButton` guard), L635–641 (`PositionPlaybackButton` guard)
- **Description**: `Window_StateChanged` hides the button for `WindowState.Minimized` (L330) but only re-shows it for `WindowState.Normal` (L334–340). `WindowState.Maximized` falls through both branches, and no other code path calls `ShowPlaybackButton()` after startup — `PositionPlaybackButton()` returns immediately at L637 because `IsVisible` is still `false`, and the `Loaded` handler (L88) fires only once.

  `Maximized` is reachable for this window: `MainWindow.xaml` uses `ResizeMode="CanResize"`, so WPF includes `WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX` in the window style even with `WindowStyle="None"`. The user can therefore maximize the tracker with <kbd>Win</kbd>+<kbd>↑</kbd>, or by Aero-Snapping it to the top edge — `Window_PreviewMouseDown` calls `DragMove()` (L200), which posts `WM_SYSCOMMAND`/`SC_MOUSEMOVE` and enters the standard `DefWindowProc` move loop that honours Aero Snap.

  Sequence: maximize the tracker → Minimize menu item (L318–323, sets `WindowState = Minimized`, button hidden at L330) → restore from the taskbar → `WindowState` returns to `Maximized`, not `Normal` → neither branch runs → the Play/Pause button never comes back. It stays gone until the user happens to restore the window to `Normal`. The same fall-through also leaves the pre-existing `ShowInTaskbar = false` / `Topmost = true` restoration un-run, which is a pre-existing gap but is now user-visible because a control silently disappears.

  The macOS side does not have this hole: `windowDidDeminiaturize` unconditionally calls `showPlaybackButton()` (`TimerWindow.swift` L543–547).
- **Risk**: The advertised primary control vanishes permanently in a normal minimize/restore cycle, with no error, no log, and no way for the user to get it back other than discovering that they must un-maximize the window. The user is left with only the middle-click and context-menu paths, which is the exact regression this feature was meant to remove.
- **Suggested Fix**: Invert the guard so the non-minimized case is handled generically, e.g. return early on `WindowState.Minimized` and otherwise run the restore path (`HideStatsWindow(); ShowInTaskbar = false; Topmost = true; ShowPlaybackButton();`) for both `Normal` and `Maximized`. Add a regression check that toggles `WindowState` through `Maximized → Minimized → Maximized` and asserts `playbackButtonWindow.IsVisible`.

### Issue 3: The button window is made visible before it is positioned, so its first frame paints at an unrelated location
- **Severity**: Low
- **File**: `MiniStopwatch.App/MainWindow.xaml.cs`, `MiniStopwatch.App/PlaybackButtonWindow.xaml`, `macos/Sources/TimerWindow.swift`
- **Line(s)**: `MainWindow.xaml.cs` L621–632 and L637–641; `PlaybackButtonWindow.xaml` L11–13 (`WindowStartupLocation="Manual"` with no `Left`/`Top`); `TimerWindow.swift` L440–442
- **Description**: `PositionPlaybackButton()` bails out at L637 when `!playbackButtonWindow.IsVisible`, which forces `ShowPlaybackButton()` to call `Show()` (L627) *before* it can position the window (L632). `PlaybackButtonWindow.xaml` declares `WindowStartupLocation="Manual"` but never sets `Left`/`Top`, so on the very first show both dependency properties are still at their `Double.NaN` default and WPF creates the HWND with an OS-chosen default position rather than next to the tracker. The window is therefore composited at least once at the wrong place before `SetWindowPos` moves it.

  The macOS path has the same shape in `TimerWindowController.init()`: `window.addChildWindow(playbackWindow, ordered: .above)` (L440) orders the panel in, and only then does `positionPlaybackButton()` (L441) move it from its `NSRect(x: 0, y: 0, ...)` construction origin. Because `init()` runs before `showWindowAndActivate()` (`AppDelegate.swift` L10–13), the panel can also be ordered on screen before the timer window it is supposed to be attached to.
- **Risk**: A 30×30 always-on-top Play button flashes at the wrong screen location (bottom-left of the screen on macOS, an OS cascade position on Windows) every cold start. Cosmetic only, but it is the first thing a user sees of the new feature and it looks like a rendering defect.
- **Suggested Fix**: Compute and assign the position before making the window visible. On Windows, either seed `Left`/`Top` in `ShowPlaybackButton()` prior to `Show()`, or relax the `IsVisible` guard in `PositionPlaybackButton()` to an `Owner != null` check so the position can be set first. On macOS, call `positionPlaybackButton()` before `addChildWindow`, and defer the initial `orderFront` to `showWindowAndActivate()`/`showPlaybackButton()` so the panel is never on screen before its parent.

### Issue 4: When neither side has room, the clamp parks the button on top of the clock instead of keeping it clear
- **Severity**: Low
- **File**: `MiniStopwatch.App/MainWindow.xaml.cs`, `macos/Sources/TimerWindow.swift`
- **Line(s)**: `MainWindow.xaml.cs` L643–658; `TimerWindow.swift` L1020–1037
- **Description**: The placement logic tries right-of-tracker, flips to left-of-tracker if the right side overflows, and then unconditionally `Math.Clamp`s the result into the work area. The clamp has no knowledge of the tracker's own rectangle, so when the flipped-left coordinate (`Left - 30 - 2`) falls outside the work area it is clamped back to `workArea.Left` — which is inside the tracker when the tracker is itself against the left edge and wide enough to fill the remaining width. The button then covers the tracker's status indicator and part of the digits, and because the button is an owned/topmost window it wins the z-order. This is reachable whenever the tracker is maximized (see Issue 2) or on a narrow display with a user-resized tracker.

  The macOS version is worse in one specific state: `positionPlaybackButton()` falls back to `?? parentFrame` when both `parentWindow.screen` and `NSScreen.main` are nil (`TimerWindow.swift` L1022–1024). With `visibleFrame == parentFrame`, the right-side test `parentFrame.maxX + 2 + 30 > parentFrame.maxX` is *always* true, the flip yields `parentFrame.minX - 32`, the clamp pulls it back to `parentFrame.minX`, and `y` resolves to `parentFrame.maxY - 30` — a guaranteed full overlap of the clock face. `positionStatsWidget()` (L979) deliberately keeps the optional and uses `visibleFrame.map { ... }` so it degrades to the unclamped desired position instead; the new code discarded that safety.
- **Risk**: The timer digits and the running/paused status dot become unreadable, and the button sits over the drag surface, in a state the user cannot correct by moving the window (the clamp re-applies on every `LocationChanged`/`windowDidMove`).
- **Suggested Fix**: After clamping, test the resulting button rectangle against the tracker rectangle and, on intersection, fall back to a placement that stays clear (e.g. anchor it inside the work area but above/below the tracker, or hide it) rather than overlapping. On macOS, drop the `?? parentFrame` fallback and use the same `visibleFrame.map { ... }` optional-preserving pattern already used by `positionStatsWidget()` so that "no screen information" means "do not clamp" rather than "clamp to the parent window".

### Issue 5: macOS hover feedback is dead code — `isHovering` never affects rendering
- **Severity**: Low
- **File**: `macos/Sources/TimerWindow.swift`
- **Line(s)**: L300–301 (and the state it feeds: L190, L214, L219)
- **Description**:
  ```swift
  let background = isHovering ? NSColor.white : NSColor.white.withAlphaComponent(0.9)
  background.withAlphaComponent(controlEnabled ? (isPressed ? 0.72 : 1) : 0.58).setFill()
  ```
  `withAlphaComponent` returns a colour with the alpha *replaced*, not multiplied, so the second call unconditionally discards the `0.9` chosen on the non-hover branch. Both branches start from the same opaque `NSColor.white`, so the fill is byte-identical whether or not the pointer is over the button. Nothing else in `draw(_:)` reads `isHovering` (L296–320), which makes the entire hover state — the `NSTrackingArea` in `updateTrackingAreas()` (L196–209), `mouseEntered`/`mouseExited` (L213–221) and the `isHovering` field — unobservable.

  The Windows implementation *does* change the background on hover (`PlaybackButtonWindow.xaml.cs` L182–184, `NormalBackground` `#E6FFFFFF` vs `HoverBackground` `Colors.White`), so the two platforms visibly diverge.
- **Risk**: The macOS button gives no hover affordance, so on a semi-transparent floating panel the user has no confirmation the small 30×30 target is live before committing to a click that starts or stops time tracking. It also silently breaks the cross-platform parity this feature is documented to have.
- **Suggested Fix**: Compute the alpha once and apply it a single time, e.g. build the base white with the hover-dependent alpha (`isHovering ? 1.0 : 0.9`) and multiply it by the enabled/pressed factor before the single `setFill()`, or use a distinct hover fill colour as the Windows side does. A cheap guard against regressions is to derive the fill in a small pure helper that can be exercised for the hover/press/disabled matrix.

## Resolution Log
_Updated by the driving agent as findings are addressed._

### Issue 1
- **Status**: Resolved
- **What changed**: Added `IsPlaybackControlBlocked` /
  `isPlaybackControlBlocked`, derived directly from active automatic-pause
  reasons, and used it to disable both companion controls.
- **Why**: A manually paused tracker must not let Play arm a hidden automatic
  resume while lock or Focus Protection is active.
- **How verified**: Added `PlaybackControlBlockedByActivePauseReason`; the full
  Windows suite passes.

### Issue 2
- **Status**: Resolved
- **What changed**: Every non-minimized Windows state now restores and
  repositions the companion button.
- **Why**: Aero Snap or Win+Up can restore the borderless tracker as maximized,
  not only normal.
- **How verified**: The state-change path no longer gates restoration on
  `WindowState.Normal`.

### Issue 3
- **Status**: Resolved
- **What changed**: Windows positions the hidden button before `Show()`;
  macOS positions the panel before attaching and ordering it front.
- **Why**: The first painted frame must never appear at a default or stale
  screen coordinate.
- **How verified**: Positioning no longer depends on `IsVisible`, and both
  first-show call sites position before visibility.

### Issue 4
- **Status**: Resolved
- **What changed**: Both platforms now try right, left, above, and below
  placement before using an in-work-area fallback.
- **Why**: A wide tracker near both horizontal edges should keep the control
  outside whenever vertical space exists.
- **How verified**: The placement branches explicitly test each available side.

### Issue 5
- **Status**: Resolved
- **What changed**: macOS now multiplies the hover alpha by the enabled/pressed
  alpha and applies it once.
- **Why**: `withAlphaComponent` replaces alpha; applying it twice erased the
  hover distinction.
- **How verified**: Hovered and non-hovered paths now produce different surface
  alpha values.

## Areas Reviewed With No Finding

Recorded so later rounds do not re-litigate them:

- **Click-gesture races (Windows)**: press-while-enabled → auto-pause → release is safe (`isPressed` gate at L164 plus the `controlEnabled` re-check at L173 means the toggle is dropped); press-while-disabled → enable → release is safe because `isPressed` was never set. `Mouse.Capture(ButtonSurface)`/`Mouse.Capture(null)` are balanced across `MouseLeftButtonDown`/`MouseLeftButtonUp`/`MouseLeave`. Re-entrancy from `toggleTracking()` → `RefreshDisplay()` → `UpdateState()` → `ApplyAppearance()` is fully synchronous on the one UI thread.
- **Click-gesture races (macOS)**: `.mouseEnteredAndExited` without `.enabledDuringMouseDrag` means `mouseExited` is not delivered mid-press, so the `bounds.contains(...)` check in `mouseUp` (L238–240) is what correctly cancels a drag-off release. `updateState` clears `isPressed` when the control is disabled (L252–254).
- **Activation / focus stealing**: `WS_EX_NOACTIVATE` (L86–107) plus the `WM_MOUSEACTIVATE` → `MA_NOACTIVATE` hook (L110–125) match the already-shipped `StatsWindow` pattern exactly; `MA_NOACTIVATE` (3) is the correct return value (it suppresses activation without eating the click). `ShowActivated="False"` prevents the initial `Show()` from stealing focus. On macOS, `canBecomeKey`/`canBecomeMain` returning `false` on a `.nonactivatingPanel` achieves the same.
- **Stats-window interaction**: clicking the button cannot fire `Window_Deactivated`/`Window_PreviewMouseDown` on `MainWindow` (the button never activates), which is why the toggle callback explicitly calls `HideStatsWindow()` first (`MainWindow.xaml.cs` L83–87) — correct. macOS mirrors it with `dismissStatsWidget()` (L436–439).
- **Owner close / shutdown**: `playbackButtonWindow` is an owned WPF window, so it is closed automatically with `MainWindow`; `Window_Closed` in `PlaybackButtonWindow` removes the `HwndSource` hook. `displayTimer` is stopped in `MainWindow.Window_Closed`, and `OnBrowserActivityChanged` is `isClosing`-guarded. `UpdateState` on an already-closed window only touches live `FrameworkElement` properties, so no crash window exists. On macOS both `prepareForTermination()` and `deinit` close the controller, and `NSPanel` defaults `isReleasedWhenClosed` to `false`, so there is no over-release.
- **Opacity propagation**: `playbackButtonWindow` is assigned (L83) before the first `SetOpacity` call, so L533 cannot NRE; `ShowPlaybackButton` re-syncs opacity on every show (L631). macOS `applyOpacity` (L916–919) runs after the stored-property initialisation of `playbackButtonController`.
- **Field-initialisation ordering**: `Window_SizeChanged`/`Window_LocationChanged` are layout/HWND-driven and cannot fire before the `readonly` `playbackButtonWindow` field is assigned in the constructor, so `PositionPlaybackButton()` cannot dereference null.
- **DPI**: `GetCurrentMonitorWorkArea()` correctly normalises the monitor rect by `GetDpiForWindow` and both windows are clamped into the *same* monitor's work area, so the WPF `Left`/`Top` logical-unit space cannot diverge between the tracker and the button under per-monitor DPI.
- **Monitor edges / parent movement / resize**: `Window_SizeChanged` and `Window_LocationChanged` both re-run `PositionPlaybackButton()`, including during live `WM_SIZING`/`DragMove` loops and during monitor disconnects (which move the owner and therefore raise `LocationChanged`). macOS covers the same via `windowDidMove`/`windowDidResize`.
- **Child-window ordering**: owned Win32 windows are always above their owner, and both windows carry `Topmost`, so the button cannot be occluded by the tracker. `showPlaybackButton()` on macOS repairs the parent link (`playbackWindow.parent !== parentWindow`) after the explicit `orderOut(nil)` in `windowWillMiniaturize`, and re-syncs `level` and `alphaValue`.
- **32-bit P/Invoke risk**: `GetWindowLongPtrW`/`SetWindowLongPtrW` are not exported by 32-bit `user32.dll`, but `build.ps1` publishes `-r win-x64` only, so `EntryPointNotFoundException` is unreachable for shipped builds.
- **Timer completion**: `isAtZero` is computed as `displayTime < TimeSpan.FromSeconds(1)`, identical to the existing `ToggleMenuItem.Header` logic (`MainWindow.xaml.cs` L419–424), and `IsTimerCompleted` correctly takes precedence in the tooltip ladder, so the button label always agrees with the menu.
- **Docs**: `.context-playback-button` (`docs/styles.css` L865–879) resolves against `.context-timer`, which is `position: absolute` (L835–849), and being absolutely positioned it is removed from the parent's flex flow, so the mock does not disturb the existing layout.

_Note: per the round-3 prompt this artifact should be committed alongside the reviewed code. The review request for this run explicitly scoped the reviewer to writing this artifact only ("Do not modify source files"), so no `git add`/`git commit` was performed here; committing this file is left to the driving agent._

---

# Re-Review — Round 3 Verification Pass (post-fix)

## Review Summary
- **Round**: 3 (verification pass over the fixed diff)
- **Theme**: Edge cases & robustness
- **Mode**: sequential
- **Model**: Claude Opus 5 (slot 3, latest Anthropic Opus, highest reasoning setting)
- **Artifact**: C:\Users\gapat.FAREAST\MiniStopwatch\reviews\task-290-attempt-1-review-3-claude-opus-5.md
- **Prior Issues Verified**: 5 of 5 confirmed fixed
- **New Issues Found**: 1 (Low)
- **Verdict**: ISSUES_FOUND (Low only; all five round-3 findings are resolved)

## Evidence Checklist
- [x] Re-enumerated the change set on the fixed tree: `git --no-pager status` / `git --no-pager diff --stat` in `C:\Users\gapat.FAREAST\MiniStopwatch` — 14 modified files, 2 untracked new files (`MiniStopwatch.App/PlaybackButtonWindow.xaml{,.cs}`); `macos/Sources/TimerWindow.swift` now +264 lines.
- [x] Re-read the full fixed diff for the behavioural files: `git --no-pager diff -- MiniStopwatch.App/MainWindow.xaml.cs MiniStopwatch.Core/StopwatchController.cs MiniStopwatch.Core/TrackingController.cs MiniStopwatch.Tests/Program.cs` and `git --no-pager diff -- macos/Sources/TimerWindow.swift macos/Sources/TimerEngine.swift`.
- [x] Re-read the untracked control in full: `MiniStopwatch.App/PlaybackButtonWindow.xaml` (30x30 `Window`, `Width`/`Height` explicitly set so the pre-`Show()` position maths cannot read `NaN`) and `PlaybackButtonWindow.xaml.cs`.
- [x] Built the solution: `dotnet build MiniStopwatch.sln -c Release` -> **Build succeeded, 0 Warning(s), 0 Error(s)**.
- [x] Ran the test project: `dotnet run --project MiniStopwatch.Tests -c Release --no-build` -> **All 26 tests passed**, including the new `Playback control is blocked by active pause reasons`.
- [x] Verified the Issue 1 state machine end to end: `StopwatchController.cs:27`, `TrackingController.cs:16`, `MainWindow.xaml.cs:427`, `TimerEngine.swift:26`, `TimerWindow.swift:789`.
- [x] Verified the Issue 2 fix by reading `Window_StateChanged` (`MainWindow.xaml.cs:325-341`) — the early return is now scoped to `Minimized` only.
- [x] Verified the Issue 3 fix by reading `ShowPlaybackButton` (`MainWindow.xaml.cs:611-631`, position at 623 before `Show()` at 626), `PositionPlaybackButton` (`MainWindow.xaml.cs:633`, `IsVisible` gate removed) and `TimerWindow.swift:441-443` (`positionPlaybackButton()` before `addChildWindow`/`orderFront`).
- [x] Verified the Issue 4 fix by hand-tracing all four placement branches on both platforms (`MainWindow.xaml.cs:640-679`, `TimerWindow.swift:1015-1053`), including proving both `Math.Clamp` calls cannot throw (`max` argument is wrapped in `Math.Max`, so `min <= max` always holds).
- [x] Verified the Issue 5 fix at `TimerWindow.swift:300-302` — a single multiplied `surfaceAlpha` is now applied once.
- [x] Independently confirmed the new Low finding against the WPF reference source: fetched `dotnet/wpf` `PresentationFramework/System/Windows/Window.cs` and read `WmMoveChangedHelper()` (guards `SetValue(LeftProperty/TopProperty)` and `OnLocationChanged` behind `WindowState == WindowState.Normal`). Corroborated in-repo by `MainWindow.SaveWindowSize` (`MainWindow.xaml.cs:840-843`), which already switches to `RestoreBounds` when the state is not `Normal`.
- [x] Re-checked threading on the widened macOS refresh path: `FocusProtection.swift:260` marshals the socket callback with `DispatchQueue.main.async`, so `refreshDisplay()` -> `playbackButtonController.buttonView.updateState(...)` stays on the main thread. No finding.
- [x] Re-checked the new `SourceInitialized` throw in `PlaybackButtonWindow.xaml.cs` against the shipped `StatsWindow.xaml.cs` — byte-identical pattern (`HwndSource.FromHwnd(handle) ?? throw`). Consistent with existing code; no finding.
- [ ] Did not run the macOS build (`macos/build-package.sh`) — no macOS host or Swift toolchain in this Windows review environment. macOS verification is by source reading and Windows parity, as in the original pass.

## Verification of Prior Findings

### Issue 1 — Play/Pause button arms an automatic-resume it can never cancel — **CONFIRMED FIXED**
`StopwatchController.IsPlaybackControlBlocked => automaticPauseReasons.Count > 0` (`StopwatchController.cs:27`) is now a true "a pause reason is active" predicate, independent of `resumeAfterAutomaticPause`. It is surfaced through `TrackingController.IsPlaybackControlBlocked` (`TrackingController.cs:16`) and consumed at `MainWindow.xaml.cs:427`; macOS mirrors it exactly (`TimerEngine.swift:26-28`, `TimerWindow.swift:789`). The trap is closed at its root: with a reason active the control is disabled *before* the user can reach `Start()`, so `resumeAfterAutomaticPause` can never be armed from the button, and when the last reason clears the control re-enables. The new unit test `PlaybackControlBlockedByActivePauseReason` (`MiniStopwatch.Tests/Program.cs:300-315`) asserts exactly the reported reproduction (paused tracker + distracting site => `IsAutomaticallyPaused == false` but `IsPlaybackControlBlocked == true`) and passes. `README.md` was updated to state the implemented rule ("disabled whenever a lock or Focus Protection pause reason is active, even if tracking was already paused manually"), so code and documentation now agree.

### Issue 2 — Button hidden forever after restoring to `Maximized` — **CONFIRMED FIXED**
`Window_StateChanged` (`MainWindow.xaml.cs:325-341`) now returns early only for `WindowState.Minimized`; every other state falls through to `HideStatsWindow(); ShowInTaskbar = false; Topmost = true; ShowPlaybackButton();`. The `Maximized -> Minimized -> Maximized` cycle therefore restores the control, and the pre-existing `ShowInTaskbar`/`Topmost` restoration gap for `Maximized` is closed with it. (See new Issue 6 below for the placement of that now-reachable maximized case.)

### Issue 3 — Window shown before it is positioned — **CONFIRMED FIXED**
Windows: the `IsVisible` gate is gone from `PositionPlaybackButton` (it now only rejects `Minimized`, `MainWindow.xaml.cs:633-638`), and `ShowPlaybackButton` positions at line 623 before calling `Show()` at line 626. `PlaybackButtonWindow.xaml` sets explicit `Width="30" Height="30"`, so the pre-show arithmetic reads real values rather than `NaN`, and WPF applies `Left`/`Top` at HWND creation under `WindowStartupLocation="Manual"`. macOS: `positionPlaybackButton()` now runs before `window.addChildWindow(...)` and `orderFront(nil)` (`TimerWindow.swift:441-443`), so the panel is never composited at its `NSRect(x: 0, y: 0, ...)` construction origin.

### Issue 4 — Clamp parks the button on top of the clock — **CONFIRMED FIXED**
Both platforms now try right (`MainWindow.xaml.cs:648-653` / `TimerWindow.swift:1030-1034`), then left (`656-662` / `1036-1040`), then fully above (`670-675` / `1046-1050`), then fully below (`677-680` / `1052-1055`), and only fall back to the overlapping in-work-area position when no side has room at all. I verified the non-overlap geometry for the default 184x58 tracker: the right placement starts at `Left + width + 2`, i.e. entirely outside the tracker, so it does not cover the `WM_NCHITTEST` resize grips produced by `GetResizeHitTest`. I also verified both `Math.Clamp` calls are throw-safe because the `max` argument is wrapped in `Math.Max`.

### Issue 5 — macOS hover feedback is dead code — **CONFIRMED FIXED**
`TimerWindow.swift:300-302` now computes `surfaceAlpha = (isHovering ? 1 : 0.9) * (controlEnabled ? (isPressed ? 0.72 : 1) : 0.58)` and applies it in a single `withAlphaComponent` call, so hovered and non-hovered states render at measurably different alphas (1.0 vs 0.9 enabled-idle) and parity with the Windows `HoverBackground`/`NormalBackground` pair is restored.

## Issues (new in the current diff)

### Issue 6: While maximized, the button is positioned from the restore rectangle, not the maximized one
- **Severity**: Low
- **File**: `MiniStopwatch.App/MainWindow.xaml.cs`
- **Line(s)**: 640-641 (`trackerWidth`/`trackerHeight` from `ActualWidth`/`ActualHeight`), 643-647 (`preferredTop` from `Top`), 648 (`right` from `Left`), 656 (`left` from `Left`), 665 (fallback `Left`)
- **Description**: `PositionPlaybackButton` mixes two different coordinate sources. It takes the tracker size from `ActualWidth`/`ActualHeight`, which track the real maximized size, but takes the tracker origin from `Left`/`Top`. WPF only writes the `Left`/`Top` dependency properties while the window is in the `Normal` state — `Window.WmMoveChangedHelper()` in the WPF reference source wraps `SetValue(LeftProperty, ...)`, `SetValue(TopProperty, ...)` and `OnLocationChanged` in `if (WindowState == WindowState.Normal)`. While the tracker is maximized, `Left`/`Top` therefore still hold the pre-maximize restore origin. The same file already encodes this knowledge: `SaveWindowSize` (`MainWindow.xaml.cs:840-843`) deliberately switches to `RestoreBounds` whenever the state is not `Normal`.

  This path only became reachable through the Issue 2 fix: `Window_StateChanged` now calls `ShowPlaybackButton()` for `Maximized`, and `Window_SizeChanged` fires on maximize because `ActualWidth`/`ActualHeight` change (`Window_LocationChanged` does not fire, for the same reason the DPs are not updated). Worked example on a 1920x1040 work area with a restore rectangle of (1200, 700, 184, 58): after Win+Up, `right = 1200 + 1920 + 2 = 3122` overflows, the left flip yields `1168`, which passes `>= workArea.Left`, so the button is parked at (1168, 692) — floating over the middle of the maximized clock rather than beside it. With a restore origin near the left edge the fallback branch instead pins it to the far right edge at a `Top` derived from the stale restore `Top`. Both outcomes are arbitrary with respect to the window the button is supposed to be attached to.
- **Risk**: Cosmetic and self-correcting — restoring the window to `Normal` fires `SizeChanged`/`LocationChanged` and re-syncs the position — but for the whole time the tracker is maximized the "attached" control is visibly detached, and in the common case it overlaps the digits it was explicitly redesigned (Issue 4) not to cover. It also silently weakens the Issue 4 guarantee, because the four-way placement search is fed a rectangle that does not exist on screen.
- **Suggested Fix**: Derive the tracker rectangle once from a state-aware source and use it for both origin and size — e.g. `var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;` is *not* sufficient for `Maximized` (RestoreBounds is also the restore rect); prefer reading the live frame with `GetWindowRect` on the tracker HWND and converting with the same DPI scale already used by `GetCurrentMonitorWorkArea()`, or use `PointToScreen(new Point(0, 0))` to obtain the true on-screen origin. The same substitution should be applied to the pre-existing `PositionStatsWindow` (`MainWindow.xaml.cs:588-608`), which shares the pattern but is masked today because the stats window is hidden on every state change.

## Resolution Log (verification pass)

### Issue 1
- **Status**: Fixed — verified
- **What changed**: `IsPlaybackControlBlocked` / `isPlaybackControlBlocked` added to `StopwatchController`, `TrackingController` and `TimerEngine`; consumed by both companion controls; README aligned.
- **Why**: The gate must reflect "a pause reason is active", not "a deferred resume is armed".
- **How verified**: Code trace of the original reproduction now stops at the disabled control; `dotnet build -c Release` clean; `dotnet run --project MiniStopwatch.Tests -c Release` -> 26/26 pass including the new `Playback control is blocked by active pause reasons`.

### Issue 2
- **Status**: Fixed — verified
- **What changed**: `Window_StateChanged` early-returns only for `Minimized`.
- **Why**: `Maximized` is reachable via Win+Up / Aero Snap on a `ResizeMode="CanResize"` window.
- **How verified**: Read `MainWindow.xaml.cs:325-341`; no state other than `Minimized` can now skip `ShowPlaybackButton()`.

### Issue 3
- **Status**: Fixed — verified
- **What changed**: `IsVisible` gate removed from `PositionPlaybackButton`; positioning moved ahead of `Show()` on Windows and ahead of `addChildWindow`/`orderFront` on macOS.
- **Why**: The first composited frame must already be at the correct coordinates.
- **How verified**: Read `MainWindow.xaml.cs:611-638` and `TimerWindow.swift:441-443`; confirmed `PlaybackButtonWindow.xaml` sets explicit `Width`/`Height` so the pre-show maths cannot read `NaN`.

### Issue 4
- **Status**: Fixed — verified
- **What changed**: Four-way placement search (right, left, above, below) before the in-work-area last resort, on both platforms.
- **Why**: A wide tracker pinned against both horizontal edges should still keep the control clear of the digits.
- **How verified**: Hand-traced every branch on both platforms; proved both `Math.Clamp` calls cannot throw; confirmed the right/left/above/below placements never intersect the tracker rectangle.
- **Residual (not raised as an issue)**: macOS kept the `?? parentFrame` screen fallback (`TimerWindow.swift:1024-1026`). If both `parentWindow.screen` and `NSScreen.main` are nil the search degenerates to the overlapping last resort — the same last-resort behaviour Windows has, and only reachable with no attached screen, so it no longer warrants a finding.

### Issue 5
- **Status**: Fixed — verified
- **What changed**: Single multiplied `surfaceAlpha` applied once in `draw(_:)`.
- **Why**: `withAlphaComponent` replaces alpha rather than multiplying it.
- **How verified**: Read `TimerWindow.swift:296-305`; hovered/idle now differ (1.0 vs 0.9) and press/disabled scaling is preserved.

### Issue 6
- **Status**: Resolved
- **What changed**: `PositionPlaybackButton()` now uses a live
  `GetWindowRect`-based tracker rectangle converted with the same per-window DPI
  scale as the monitor work area.
- **Why**: WPF preserves `Left` and `Top` as restore coordinates while maximized,
  so they cannot anchor an attached companion window in that state.
- **How verified**: The companion's origin and size now come from one live native
  rectangle in normal, snapped, and maximized states; the Windows build and full
  test suite pass.

## Additional Areas Re-checked With No Finding
- **macOS main-thread safety of the widened refresh path**: `FocusProtection.swift:260` marshals the socket callback via `DispatchQueue.main.async` before `refreshDisplay()` reaches `playbackButtonController.buttonView.updateState(...)`.
- **`SourceInitialized` throw in the new window**: identical to the shipped `StatsWindow.xaml.cs` pattern; not a new risk.
- **Shutdown / owned-window lifetime**: `App.xaml.cs` shows `MainWindow` unconditionally, so `Loaded` always fires and `playbackButtonWindow.Owner` is always assigned before any close, keeping the extra `Application.Windows` entry from blocking `ShutdownMode.OnLastWindowClose`.
- **`UpdateState` at 10 Hz**: every assignment uses interned literals or `static readonly` brushes, so the dependency-property writes are no-ops after the first change; no measurable cost and no allocation churn beyond one enumerator per tick.
- **macOS child-window re-attachment**: `showPlaybackButton()` still repairs `playbackWindow.parent !== parentWindow` after the explicit `orderOut(nil)` in `windowWillMiniaturize`, and re-syncs `level` and `alphaValue`.
- **Test coverage of the new predicate**: `PlaybackControlBlockedByActivePauseReason` covers the exact Issue 1 reproduction and both the set and clear transitions.

_As with the original pass, this run was explicitly scoped to "do not modify source files", so no `git add`/`git commit` was performed; committing this artifact alongside the reviewed code remains with the driving agent._

---

# Re-Review — Round 3 Verification Pass 2 (post-Issue-6 fix)

## Review Summary
- **Round**: 3 (second verification pass over the fixed diff)
- **Theme**: Edge cases & robustness
- **Mode**: sequential
- **Model**: Claude Opus 5 (slot 3, latest Anthropic Opus, highest reasoning setting)
- **Artifact**: C:\Users\gapat.FAREAST\MiniStopwatch\reviews\task-290-attempt-1-review-3-claude-opus-5.md
- **Prior Issues Verified**: 6 of 6 confirmed fixed (Issues 1-5 remain fixed; Issue 6 now fixed)
- **Issues Found**: 0
- **Verdict**: CLEAN

## Evidence Checklist
- [x] Re-enumerated the change set on the current tree: `git --no-pager status` / `git --no-pager diff --stat` in `C:\Users\gapat.FAREAST\MiniStopwatch` — 14 modified files (+464/-27), 2 untracked new files (`MiniStopwatch.App/PlaybackButtonWindow.xaml{,.cs}`).
- [x] Re-read the complete current diff for every behavioural file: `git --no-pager diff -- MiniStopwatch.App/MainWindow.xaml.cs`, `-- MiniStopwatch.Core/StopwatchController.cs MiniStopwatch.Core/TrackingController.cs MiniStopwatch.Tests/Program.cs README.md`, `-- macos/Sources/TimerWindow.swift macos/Sources/TimerEngine.swift`, and `-- MiniStopwatch.App/app.manifest docs/ macos/README.md VERSION browser-extension/manifest.json`.
- [x] Read the Issue 6 fix in full: `GetCurrentWindowBounds()` (`MainWindow.xaml.cs:856-878`) and its single consumer `PositionPlaybackButton()` (`MainWindow.xaml.cs:633-679`), plus the `GetWindowRect`/`GetDpiForWindow` P/Invoke declarations (`MainWindow.xaml.cs:921-926`) and the `WindowRect` struct.
- [x] Built the solution: `dotnet build MiniStopwatch.sln -c Release` -> **Build succeeded, 0 Warning(s), 0 Error(s)**.
- [x] Ran the test project: `dotnet run --project MiniStopwatch.Tests -c Release --no-build` -> **All 26 tests passed**, including `Playback control is blocked by active pause reasons`.
- [x] Established the app's DPI-awareness mode before judging the Issue 6 DPI maths: `MiniStopwatch.App/app.manifest` declares no `<dpiAware>`/`<dpiAwareness>` element and `MiniStopwatch.App.csproj` sets no `HighDpiMode`, so the process is system-DPI-aware and `GetDpiForWindow` returns one uniform scale for every window in the process. The new conversion therefore cannot diverge from WPF's own logical-unit space, nor from `GetCurrentMonitorWorkArea()`.
- [x] Verified the Issue 6 fix is behaviour-preserving in the `Normal` state: WPF `Window.ActualWidth`/`ActualHeight` are the *outer* (window-rect) dimensions and `Left`/`Top` are the window-rect origin, so for this `WindowStyle="None"` + `AllowsTransparency="True"` window `GetWindowRect`/dpi reproduces exactly the values the previous code used. No regression for the common case.
- [x] Hand-traced the maximized case end to end on a 1920x1040 work area (window rect inflated by the `WS_THICKFRAME` border, origin at `workArea.Left-8`/`workArea.Top-8`): right branch overflows, left branch underflows, `above` underflows, `below` overflows, so the button lands at the work-area top-right corner (`Left = workArea.Right - 30`, `Top = workArea.Top`) — a bounded, on-screen, Issue-4-consistent last resort driven by the *live* rectangle, not the stale restore rectangle.
- [x] Re-proved both `Math.Clamp` calls in `PositionPlaybackButton` are throw-safe (each `max` argument is wrapped in `Math.Max(min, ...)`, so `min <= max` always holds) and that the `GetCurrentWindowBounds` fallback cannot throw (`Rect(x, y, NaN, NaN)` does not trip the negative-dimension guard, and `Width`/`Height` are set in `MainWindow.xaml`).
- [x] Verified the `out var windowRect` definite-assignment path in `GetCurrentWindowBounds` — the short-circuit `||` means the struct is only read when `GetWindowRect` returned `true`; the compiler agrees (0 warnings).
- [x] Re-verified Issues 1-5 against the current tree: `StopwatchController.cs:27` / `TrackingController.cs:16` / `MainWindow.xaml.cs:427` / `TimerEngine.swift:26` / `TimerWindow.swift:788` (Issue 1); `MainWindow.xaml.cs:325-341` (Issue 2); `MainWindow.xaml.cs:622-626` and `PlaybackButtonWindow.xaml:5-6` and `TimerWindow.swift:441-443` (Issue 3); `MainWindow.xaml.cs:640-679` and `TimerWindow.swift:1015-1053` (Issue 4); `TimerWindow.swift:300-302` (Issue 5).
- [x] Investigated and **disproved** a candidate finding about the Windows press/release gesture (`PlaybackButtonWindow.xaml.cs:155,170,173`) by reading the WPF reference source `dotnet/wpf` `PresentationCore/System/Windows/Input/MouseDevice.cs` — see "Additional Areas Re-checked With No Finding" below.
- [x] Re-checked the macOS `init()` ordering: `super.init(window:)` and `restoreWindowFrame()` both run before `positionPlaybackButton()` (`TimerWindow.swift:427-443`), so `self.window` and the restored parent frame are valid at first placement, and `applyOpacity(loadOpacity())` runs after the stored `playbackButtonController` property is initialised.
- [ ] Did not run the macOS build (`macos/build-package.sh`) — no macOS host or Swift toolchain in this Windows review environment. macOS verification is by source reading and Windows parity, as in the previous passes.

## Verification of Prior Findings

### Issue 6 — Button positioned from the restore rectangle while maximized — **CONFIRMED FIXED**
`PositionPlaybackButton()` no longer mixes coordinate sources. Both the origin and the size now come from one live native rectangle produced by `GetCurrentWindowBounds()` (`MainWindow.xaml.cs:856-878`), which calls `GetWindowRect` on the tracker HWND and normalises it with `GetDpiForWindow(handle) / 96d` — byte-for-byte the same conversion `GetCurrentMonitorWorkArea()` (`MainWindow.xaml.cs:880-903`) applies to the monitor work area, so the placement search and the clamp operate in a single, consistent coordinate space. The stale `Left`/`Top` dependency properties (which WPF only updates while `WindowState == WindowState.Normal`) are gone from this path.

Three properties I verified beyond the fix's stated intent:
1. **No regression in the `Normal` state.** `Window.ActualWidth`/`ActualHeight` include the non-client area and `Left`/`Top` are the window-rect origin, so for this borderless layered window the new values equal the old ones. The change is a strict superset.
2. **DPI is sound.** The manifest opts into no per-monitor awareness, so `GetDpiForWindow` yields one process-wide scale; `trackerBounds`, `workArea` and the `playbackButtonWindow.Left`/`Top` assignment all live in the same logical-unit space. There is no mixed-DPI divergence to exploit.
3. **Defensive paths hold.** `dpiScale <= 0` degrades to `1` (matching `GetCurrentMonitorWorkArea`), a null HWND or a failed `GetWindowRect` degrades to the previous `Left`/`Top`/`ActualWidth`/`ActualHeight` values, and neither branch can throw.
The stated residual scope — `PositionStatsWindow` (`MainWindow.xaml.cs:578-608`) still reading `Left`/`Top`/`ActualWidth` — remains masked because `Window_StateChanged` hides the stats window on every state transition before the tracker can reach a non-`Normal` state, so it stays a non-issue rather than a deferred defect.

### Issues 1-5 — **ALL STILL FIXED**
- **Issue 1**: `IsPlaybackControlBlocked => automaticPauseReasons.Count > 0` (`StopwatchController.cs:27`) is still the predicate wired through `TrackingController.cs:16` to `MainWindow.xaml.cs:427` and mirrored at `TimerEngine.swift:26-28` / `TimerWindow.swift:788-793`. `PlaybackControlBlockedByActivePauseReason` passes.
- **Issue 2**: `Window_StateChanged` (`MainWindow.xaml.cs:325-341`) still early-returns only for `Minimized`; `Maximized` restores and repositions the control.
- **Issue 3**: `PositionPlaybackButton()` is still called at `MainWindow.xaml.cs:622` before `Show()` at 626, the `IsVisible` gate is still absent from `PositionPlaybackButton`, `PlaybackButtonWindow.xaml` still sets explicit `Width="30" Height="30"`, and macOS still positions before `addChildWindow`/`orderFront`.
- **Issue 4**: The four-way placement search (right / left / above / below, then in-work-area last resort) is intact on both platforms and is now fed a live rectangle, which strengthens rather than weakens the guarantee.
- **Issue 5**: `surfaceAlpha = (isHovering ? 1 : 0.9) * (controlEnabled ? (isPressed ? 0.72 : 1) : 0.58)` is still applied through a single `withAlphaComponent` call (`TimerWindow.swift:300-302`).

## Issues
None. No new edge-case, boundary, concurrency or failure-mode defect was found in the current diff.

## Resolution Log (verification pass 2)

### Issue 6
- **Status**: Fixed — verified
- **What changed**: `GetCurrentWindowBounds()` added (`MainWindow.xaml.cs:856-878`) and made the sole rectangle source for `PositionPlaybackButton()`.
- **Why**: WPF keeps `Left`/`Top` at the restore origin outside the `Normal` state, so they cannot anchor a companion window while the tracker is maximized or snapped.
- **How verified**: Confirmed equivalence with the old inputs in the `Normal` state; hand-traced the maximized geometry to a bounded on-screen result; confirmed the DPI conversion matches `GetCurrentMonitorWorkArea()` and that the process is system-DPI-aware so no cross-window scale mismatch exists; `dotnet build -c Release` clean (0 warnings) and 26/26 tests pass.

### Issues 1-5
- **Status**: Fixed — re-verified on the current tree (see "Issues 1-5 — ALL STILL FIXED" above). No regression introduced by the Issue 6 fix.

## Additional Areas Re-checked With No Finding

Recorded so later rounds do not re-litigate them:

- **Windows press-then-drag-off-then-release does *not* mis-fire (candidate finding, disproved).** `ButtonSurface_MouseLeftButtonUp` guards the toggle with `ButtonSurface.IsMouseOver` (`PlaybackButtonWindow.xaml.cs:173`) while `ButtonSurface` holds mouse capture from `MouseLeftButtonDown` (`:155`). WPF's `MouseDevice` pins `_mouseOver` to the capture element under `CaptureMode.Element` ("Always consider the mouse over the capture point"), which would normally make that guard dead code. It is not, because the handler releases capture first (`Mouse.Capture(null)` at `:170`) and `MouseDevice.ChangeMouseCapture` ends with `Synchronize()` ("Force a mouse move so we can update the mouse over"), which runs a synchronous `GlobalHitTest` through `InputManager.ProcessInput` before line 173 executes. `IsMouseOver` therefore reflects the real pointer position at the moment it is read, and a drag-off release is correctly cancelled — matching the explicit `bounds.contains(...)` check on macOS (`TimerWindow.swift:238-241`). Verified against `dotnet/wpf` `PresentationCore/System/Windows/Input/MouseDevice.cs`. The re-entrant `ButtonSurface_MouseLeave` that this synchronous re-hit-test triggers is also safe: `isPressed` is already `false` (set at `:168`) and the nested `Mouse.Capture(null)` short-circuits because `_mouseCapture` is already null, so there is no recursion and no double toggle.
- **Mixed-DPI positioning (candidate finding, disproved).** Setting `Left`/`Top` on one window from another window's DPI-normalised rectangle would be unsafe under PerMonitorV2, but `app.manifest` declares no DPI-awareness element and the csproj sets no `HighDpiMode`, so the process is system-DPI-aware and every window shares one scale factor. No stuck-on-the-wrong-monitor failure mode exists.
- **`ShowInTaskbar` churn on the minimize/restore cycle**: `MinimizeMenuItem_Click` sets `ShowInTaskbar = true` (`MainWindow.xaml.cs:321`) and the restore path sets it back to `false` (`:335`). The second assignment is now reached for `Maximized` as well as `Normal`, but WPF only reacts when the value actually changes and does not recreate the HWND for this property, so the owned-window relationship, the `WM_NCHITTEST`/`WM_WTSSESSION_CHANGE` hook and the button's `Owner` all survive the cycle. Pre-existing pattern, unchanged in substance by this diff.
- **Placement feedback loops**: `PositionPlaybackButton()` writes only to the *button* window's `Left`/`Top`, which cannot raise `LocationChanged`/`SizeChanged` on the tracker, so the `Window_SizeChanged`/`Window_LocationChanged` -> position path cannot recurse, including inside the `DragMove()` modal move loop (where `WM_MOVE` fires after the rectangle is already updated, so `GetWindowRect` is never stale).
- **`GetCurrentWindowBounds` cost**: two extra P/Invokes per `LocationChanged`/`SizeChanged`, on top of the two already spent in `GetCurrentMonitorWorkArea`. Immaterial even at mouse-move rate.
- **Shutdown ordering**: WPF closes owned windows before `MainWindow.Closed` sets `isClosing`, but nothing between those points can call `ShowPlaybackButton()`, and a late `displayTimer` tick reaching `UpdateState` only writes dependency properties on a still-live element tree, so no `InvalidOperationException` ("cannot Show a closed Window") or null dereference is reachable.
- **10 Hz `UpdateState`**: every write is either an interned string literal, a `static readonly` brush, or an unchanged `Visibility`, so `DependencyObject.SetValue` short-circuits on value equality — in particular the `ButtonSurface.ToolTip` assignment cannot make an open tooltip flicker.
- **macOS placement arithmetic**: the `min(max(...))` forms at `TimerWindow.swift:1024-1053` cannot trap the way an unguarded `Math.Clamp` would, `above`/`below` are correct for AppKit's y-up space, and `parentFrame` is the live frame in every call path (`windowDidMove`, `windowDidResize`, `showPlaybackButton`, `init`).
- **Docs and version metadata**: the 2.8.1 -> 2.9.0 bump is consistent across `VERSION`, `Directory.Build.props`, `app.manifest`, `README.md`, `docs/index.html` and `browser-extension/manifest.json`; `.context-playback-button` remains an absolutely-positioned child of the absolutely-positioned `.context-timer`, so it stays out of the flex flow.

_As with the earlier passes, this run was explicitly scoped to "do not modify source files", so no source file was touched and no `git add`/`git commit` was performed; committing this artifact alongside the reviewed code remains with the driving agent._

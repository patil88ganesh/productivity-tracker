# Code Review — Round 6 of 6 (sequential mode)

- **Task**: Add attached square Play/Pause button (task 290, attempt 1)
- **Theme**: Polish & hardening
- **Focus Area**: Performance, observability, documentation, naming clarity
- **Mode**: sequential — Slot 3 (latest Anthropic Opus)

## Review Summary
- **Round**: 6
- **Theme**: Polish & hardening
- **Mode**: sequential
- **Model**: Claude Opus 5 (highest reasoning effort)
- **Artifact**: C:\Users\gapat.FAREAST\MiniStopwatch\reviews\task-290-attempt-1-review-6-claude-opus-5.md
- **Issues Found**: 3
- **Verdict**: ISSUES_FOUND

## Evidence Checklist

- [x] Built the full solution from the working tree:
  `dotnet build MiniStopwatch.sln -c Release -v minimal` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
  Confirms `MiniStopwatch.App/PlaybackButtonWindow.xaml` is picked up by the
  implicit `UseWPF` page globs and that the new `Marshal.SetLastPInvokeError` /
  `OfType<Rectangle>` usages compile under `ImplicitUsings`.
- [x] Ran the full core suite:
  `dotnet run --project MiniStopwatch.Tests -c Release --no-build` →
  **All 26 tests passed**, including the new
  `Playback control is blocked by active pause reasons`.
- [x] **Empirically verified the Windows button icon geometry** by reconstructing
  the exact `PlaybackButtonWindow.xaml` visual tree (22×22 `Border`,
  `BorderThickness=1.5`, inner `Grid`, `Polygon Points="7,5 16,11 7,17"`
  `Stretch="None"`, and the 12×14 `PauseIcon` grid) in an STA PowerShell host
  against `PresentationFramework`, then calling `Measure`/`Arrange`/`UpdateLayout`
  and `TranslatePoint` to read the real rendered offsets. Measured results:
  `PlayIcon` `DesiredSize = 16×17` (not the 9×12 point bounds); triangle drawn at
  X `10..19` (centre 14.5) and Y `7.5..19.5` (centre 13.5) against a border centre
  of `11,11`; `PauseIcon` bars drawn at X `5..17`, Y `4..18` (centre `11,11`).
  See Issue 1.
- [x] Verified release metadata is internally consistent at 2.9.0:
  `git grep -n -E "2\.8\.[0-9]|2\.9\.0"` over tracked non-artifact paths returns
  **only 2.9.0** across `Directory.Build.props`, `MiniStopwatch.App/app.manifest`,
  `VERSION`, `README.md`, `browser-extension/manifest.json`, and `docs/index.html`.
  No stale 2.8.1 reference survives.
- [x] Verified macOS release metadata is derived, not duplicated:
  `macos/build-package.sh` reads `VERSION` into `CFBundleShortVersionString` and
  `CFBundleVersion`, so the bump propagates automatically. Confirmed the new
  macOS code lives inside the already-listed `macos/Sources/TimerWindow.swift`
  and `TimerEngine.swift`, so the explicit `swiftc` source list in
  `build-package.sh` and the `.github/workflows/build-macos.yml` path filters
  (`macos/**`, `VERSION`) need no change.
- [x] Verified documentation accuracy against behaviour:
  `README.md:26-29`'s claim that the button "is disabled whenever a lock or Focus
  Protection pause reason is active, even if tracking was already paused manually"
  matches `StopwatchController.IsPlaybackControlBlocked => automaticPauseReasons.Count > 0`
  and the two reasons in `AutomaticPauseReason`. The move/resize/minimize/opacity
  claims match `Window_LocationChanged`, `Window_SizeChanged`, `Window_StateChanged`
  and `SetOpacity` on Windows and `windowDidMove` / `windowDidResize` /
  `windowWillMiniaturize` / `applyOpacity` on macOS.
- [x] Verified the new marketing-site markup: `.context-playback-button`
  (`docs/styles.css:865`) is `position: absolute` inside `.context-timer`, which
  is itself `position: absolute` (`docs/styles.css:835`), so it anchors to the
  intended containing block; it is not a flex item so `.context-timer`'s `gap`
  is unaffected. `docs/script.js` only queries `#demoTimer`, `#demoTime`, `#year`
  and `.reveal`, so the feature-card 03 retitle and the new span break no script.
- [x] Reviewed the 10 Hz hot path. `RefreshDisplay()` (Windows, `DispatcherTimer`
  at 100 ms) and `refreshDisplay()` (macOS, `Foundation.Timer` at 0.1 s) now call
  `UpdateState`/`updateState` on every tick. On Windows every assignment inside
  `UpdateState`/`ApplyAppearance` writes an interned string, an enum, a cached
  `static readonly` brush, or an unchanged `double`, so WPF's dependency-property
  equality check short-circuits invalidation; the only per-tick allocation is the
  `OfType<Rectangle>` iterator over two children. On macOS `needsDisplay = true`
  is set unconditionally each tick, redrawing a 30×30 layer-backed panel.
  Neither is a measurable regression. **No performance finding.**
- [x] Reviewed lifecycle/ownership. On Windows `playbackButtonWindow` is
  constructed before `SetOpacity`/`RefreshDisplay` first use it; `Owner` is
  assigned on `Loaded` (always reached, since the app never starts minimized), so
  WPF's owned-window teardown closes it with `MainWindow` and `ShutdownMode.OnLastWindowClose`
  still reaches zero windows. `Window_Closed` removes the `HwndSource` hook. On
  macOS `orderOut` in `windowWillMiniaturize` detaches the child window, and both
  `showPlaybackButton()` and `showWindowAndActivate()` re-attach via the
  `playbackWindow.parent !== parentWindow` guard. **No leak or teardown finding.**
- [x] Reviewed naming clarity of the new public Core surface
  (`StopwatchController.IsPlaybackControlBlocked`, `TrackingController.IsPlaybackControlBlocked`,
  `TimerEngine.isPlaybackControlBlocked`). The three names are identical across
  platforms, the predicate is a strict superset of `IsAutomaticallyPaused`, and
  the distinction is pinned by the new test. **No finding.**
- [ ] Did not execute either GUI (no interactive Windows desktop session for the
  WPF app, and no macOS host for `swiftc`). Windows visual behaviour was instead
  verified analytically through a real WPF layout pass (see Issue 1); macOS
  geometry was verified by arithmetic against the AppKit bottom-left coordinate
  convention (see Issue 2).

## Issues

### Issue 1: Windows Play icon renders off-centre in the button, unlike the Pause icon and unlike macOS
- **Severity**: Medium
- **File**: `MiniStopwatch.App/PlaybackButtonWindow.xaml`
- **Line(s)**: 36-39 (`<Polygon x:Name="PlayIcon" Points="7,5 16,11 7,17" Stretch="None" .../>`)
- **Description**: A WPF `Shape` with `Stretch="None"` reports a natural size
  measured **from the origin**, not the bounding box of its points — the layout
  size is `(max(bounds.Right,0), max(bounds.Bottom,0))`. For
  `Points="7,5 16,11 7,17"` that is `16×17`, not the `9×12` the triangle actually
  occupies. Centering that element therefore centres a box whose leading `7,5`
  gap is empty, so the leading offset is applied *on top of* the centering.

  Measured on a real WPF layout pass over the exact tree in this file:

  | element | drawn X | drawn Y | visual centre | button centre |
  |---|---|---|---|---|
  | `PlayIcon` | `10 .. 19` | `7.5 .. 19.5` | `14.5, 13.5` | `11, 11` |
  | `PauseIcon` | `5 .. 17` | `4 .. 18` | `11, 11` | `11, 11` |

  The play triangle is **3.5 px right and 2.5 px down** of centre on a 22 px
  button (~16% of its width), leaving the left half of the button empty and the
  triangle tip 1.5 px from the inner edge of the 1.5 px border, with its bottom
  1 px from the inner edge at a 4 px corner radius. The Pause icon is exactly
  centred, and the macOS glyph (`macos/Sources/TimerWindow.swift:294-330`, play
  triangle `(11,8)-(21,15)-(11,22)` inside a square inset to `4..26`) is
  approximately centred — so the two platforms and the two states of the same
  Windows button disagree.
- **Risk**: The Play glyph is the button's **default and most-seen state** — it is
  what ships on first launch, after every manual pause, and after every reset.
  On the flagship new control of the 2.9.0 release it reads as a misaligned,
  border-crowding icon that visibly jumps when toggling to Pause (which is
  centred). This is exactly the class of defect this final polish round exists to
  catch, and it will be baked into release screenshots and the store listing.
- **Suggested Fix**: Anchor the geometry at the origin so the natural size equals
  the drawn size, e.g. `Points="0,0 9,6 0,12"` (identical shape, `DesiredSize`
  becomes `9×12`, centres exactly like `PauseIcon`). Alternatively use a `Path`
  with origin-anchored `Data`, or give the `Polygon` an explicit `Width`/`Height`
  with `Stretch="Uniform"`. After the change, re-check that the triangle's optical
  centre still reads centred (a play triangle usually wants ~1 px of extra right
  padding to look optically centred).

### Issue 2: The button's vertical overhang differs between Windows and macOS
- **Severity**: Low
- **File**: `MiniStopwatch.App/MainWindow.xaml.cs`, `macos/Sources/TimerWindow.swift`
- **Line(s)**: `MainWindow.xaml.cs:47` (`PlaybackButtonTopOffset = 8`) and
  `MainWindow.xaml.cs:644-646`; `TimerWindow.swift:1028` (`parentFrame.maxY - 18`)
- **Description**: Both platforms host the 22-unit visible square inside a 30-unit
  window/panel with a 4-unit transparent inset, and both use a 2-unit horizontal
  gap — so the horizontal result matches exactly (6 units of visible gap). The
  vertical anchors do not match:
  - **Windows**: `preferredTop = trackerBounds.Top - 8` is the *window top*, so the
    visible square spans `trackerTop - 4 .. trackerTop + 18` — a **4 px** overhang
    above the clock.
  - **macOS**: `preferredY = parentFrame.maxY - 18` is the panel *origin* (bottom
    edge, AppKit y-up), so the panel top is `maxY + 12` and the visible square
    spans `maxY + 8` down to `maxY - 14` — an **8 pt** overhang above the clock.

  The two platforms therefore attach the same control at two different heights
  relative to the clock, and neither offset is expressed in terms of the 4-unit
  inset that makes the number meaningful.
- **Risk**: Purely cosmetic, but it is a visible cross-platform parity gap in a
  release whose README and website both describe a single "attached square
  Play/Pause button at the clock's top-right". It also makes the two magic
  numbers (`8` and `18`) impossible to reconcile during future maintenance, so a
  later change to one platform will silently widen the divergence.
- **Suggested Fix**: Pick one overhang and derive both constants from it. To match
  Windows' 4-unit overhang, use `parentFrame.maxY - 22` on macOS; to match macOS'
  8-unit overhang, use `PlaybackButtonTopOffset = 12` on Windows. Give the macOS
  literals the same named-constant treatment the Windows side already has
  (`PlaybackButtonGap`, `PlaybackButtonTopOffset`) so the `2` and the top offset
  are not repeated inline in `positionPlaybackButton()`.

### Issue 3: `Mouse.Capture(null)` releases application-wide capture instead of the button's own
- **Severity**: Low
- **File**: `MiniStopwatch.App/PlaybackButtonWindow.xaml.cs`
- **Line(s)**: 141 (`ButtonSurface_MouseLeave`), 170 (`ButtonSurface_MouseLeftButtonUp`)
- **Description**: `Mouse.Capture(null)` is unconditional and process-wide — it
  releases whatever element currently holds WPF mouse capture, regardless of
  whether `ButtonSurface` ever had it. `ButtonSurface_MouseLeave` fires on every
  plain hover-out, including the common case where the user never pressed the
  button and this window holds no capture at all, so the app forcibly clears any
  capture held elsewhere (WPF's own capture-based dismissal machinery for
  `ContextMenu` / `Popup` is the realistic owner). The element-scoped API
  `UIElement.ReleaseMouseCapture()` is a no-op unless the element actually holds
  capture, which is the intended semantics here. The related
  `Mouse.Capture(ButtonSurface)` at line 155 also has its return value discarded;
  because the window is `WS_EX_NOACTIVATE` and answers `WM_MOUSEACTIVATE` with
  `MA_NOACTIVATE`, the underlying Win32 `SetCapture` will not succeed while
  another application is foreground — which is the *normal* way this button is
  used. The click still works in that case (the mouse-up lands on the same HWND
  and `isPressed`/`IsMouseOver` gate it correctly), so the failed capture is
  benign, but the code reads as if capture is guaranteed.
- **Risk**: Low and, to be explicit about confidence: I could **not** construct a
  reproducible failure in the current UI, because while a `ContextMenu` holds
  capture WPF does not route `MouseLeave` to `ButtonSurface`. The concern is that
  this is an over-broad global call sitting on a handler that fires on every
  hover-out, so any future capture-taking element added to this app (a popup,
  drag-adorner, or in-place editor) can be silently de-captured by an unrelated
  mouse movement across the Play/Pause button. It is a latent hazard, not a
  present defect.
- **Suggested Fix**: Replace both `Mouse.Capture(null)` calls with
  `ButtonSurface.ReleaseMouseCapture()`, which releases only if this element holds
  capture. Optionally set `isPressed = Mouse.Capture(ButtonSurface)`-independent
  logic explicitly, or drop the capture calls entirely — the existing
  `isPressed` + `ButtonSurface.IsMouseOver` gate already produces correct click
  semantics without capture.

## Resolution Log
_Updated by the driving agent as findings are addressed._

### Issue 1
- **Status**: Resolved
- **What changed**: Rebased the Windows Play polygon to origin-anchored points
  (`0,0 9,6 0,12`) while preserving the same triangle dimensions.
- **Why**: WPF centers the shape's measured box; removing the unused leading
  coordinates makes that box match the rendered geometry.
- **How verified**: The Windows build succeeds and the Play geometry now has a
  9×12 natural box centered by the existing alignments.

### Issue 2
- **Status**: Resolved
- **What changed**: Added named macOS layout constants for gap, surface inset,
  and top overhang, then derived the panel origin from a 4-point overhang.
- **Why**: Both platforms now position the same 22-unit square with the same
  vertical overhang and horizontal gap.
- **How verified**: The macOS formula places the panel origin at
  `parent.maxY - 22` for the current 30-point panel, matching Windows visually.

### Issue 3
- **Status**: Resolved
- **What changed**: Replaced both process-wide `Mouse.Capture(null)` calls with
  `ButtonSurface.ReleaseMouseCapture()`.
- **Why**: Hover-out or release should only relinquish capture owned by this
  control and must not disturb another WPF element.
- **How verified**: The scoped API compiles and preserves the existing
  press/release and drag-off cancellation flow.

## Notes for the driving agent

- This artifact is currently **untracked**. Per the round-6 instructions it must
  be `git add`-ed and committed together with the reviewed code; the reviewer does
  not commit.
- No source file was modified during this review.

## Re-Review (Round 6, fix verification pass — 2026-09-08)

**Model**: Claude Opus 5 (highest reasoning effort) · **Verdict**:
ISSUES_FOUND (1 new, Low)

### Fix verification

| Finding | Status | Verification |
|---|---|---|
| Issue 1 — Play icon off-centre | **Confirmed fixed** | A WPF layout pass measured the current Play icon at 9×12, centered at 11,11 like the Pause icon. |
| Issue 2 — Overhang parity | **Confirmed fixed** | Both visible squares now overhang the clock top by 4 units, with matching side and fallback gaps. |
| Issue 3 — Process-wide capture release | **Confirmed fixed** | Both releases now use `ButtonSurface.ReleaseMouseCapture()` and preserve drag-off cancellation. |

### Issue 4: macOS click target includes the transparent panel halo

- **Severity**: Low
- **File**: `macos/Sources/TimerWindow.swift`
- **Description**: The panel is 30×30 while the drawn button is 22×22, but
  default AppKit hit testing and the mouse-up check accepted the full panel.
- **Risk**: A click in the invisible four-point halo could toggle tracking and
  swallow a click intended for the window underneath.
- **Suggested Fix**: Restrict hit testing, tracking, and release containment to
  the same inset rectangle used for drawing.

### Resolution Log

#### Issue 4
- **Status**: Resolved
- **What changed**: Added shared `PlaybackButtonLayout` metrics and an
  `interactiveBounds` rectangle; `hitTest`, tracking areas, mouse-up
  containment, drawing, panel sizing, and positioning now share those metrics.
- **Why**: Only the visible 22×22 square should accept pointer interaction while
  the transparent 30×30 panel remains available for its shadow.
- **How verified**: Points in the four-point halo now return `nil` from
  `hitTest`; click completion and hover tracking use the same inset bounds.

## Final Re-Review (Round 6 — 2026-09-08)

**Model**: Claude Opus 5 (highest reasoning effort) · **Verdict**:
ISSUES_FOUND (1 new, Low)

Issues 1–4 were confirmed fixed. The shared macOS inset rectangle is used by
hit testing, tracking, mouse-up containment, drawing, panel sizing, and
positioning.

### Issue 5: `hitTest` compares coordinates from different view spaces

- **Severity**: Low
- **File**: `macos/Sources/TimerWindow.swift`
- **Description**: AppKit supplies `hitTest(_:)` points in superview
  coordinates, while `interactiveBounds` is in the button view's local
  coordinates. The current content-view placement makes those spaces identical,
  but re-hosting the view could silently break halo hit testing.

### Resolution Log

#### Issue 5
- **Status**: Resolved
- **What changed**: Converted the incoming point from the superview into local
  coordinates before comparing it with `interactiveBounds`; retained the
  original point for `super.hitTest`.
- **Why**: The containment check must be correct independently of the view's
  frame origin or future hosting hierarchy.
- **How verified**: The local rectangle and local point now share a coordinate
  space, while AppKit's superclass receives the coordinate space it expects.

---

## Final Verdict (Round 6, third fix-verification pass — 2026-09-08)

- **Model**: Claude Opus 5
- **Issues Found**: 0 new; Issues 1–5 confirmed fixed
- **Verdict**: **CLEAN — ship-ready for 2.9.0**

### Final Evidence

- [x] The Windows Play and Pause glyphs are centered in the 22×22 surface.
- [x] Windows and macOS use the same visible side gap and top overhang.
- [x] Windows capture release is scoped to the button surface.
- [x] macOS drawing, hover, hit testing, and click completion share the same
  22×22 interactive rectangle inside the transparent 30×30 shadow panel.
- [x] macOS converts AppKit's superview-space `hitTest` point to local
  coordinates before comparing it with local bounds.
- [x] The Windows solution builds with zero warnings and all 26 tests pass.
- [x] Both browser-extension test suites pass.
- [x] Version-bearing source and public documentation consistently use 2.9.0.

---

## Final Verdict (Round 6, second fix-verification pass — 2026-09-08)

## Review Summary
- **Round**: 6 (final re-review)
- **Theme**: Polish & hardening
- **Mode**: sequential — Slot 3 (latest Anthropic Opus)
- **Model**: Claude Opus 5 (highest reasoning effort)
- **Artifact**: C:\Users\gapat.FAREAST\MiniStopwatch\reviews\task-290-attempt-1-review-6-claude-opus-5.md
- **Issues Found**: 1 new (Low). Issues 1–4 all confirmed fixed.
- **Verdict**: ISSUES_FOUND (Low only) — **ship-ready**; the single new finding
  is a latent API-contract fragility with no present user-visible defect.

### Fix verification

| Finding | Status | Verification |
|---|---|---|
| Issue 1 — Play icon off-centre | **Confirmed fixed** | `PlaybackButtonWindow.xaml:36-39` now reads `Points="0,0 9,6 0,12"`. Origin-anchored, so the `Stretch="None"` natural size is `9×12` (equal to the drawn bounds) and `HorizontalAlignment/VerticalAlignment="Center"` inside the 22×22 `ButtonSurface` centres it at `11,11` — identical to `PauseIcon`. |
| Issue 2 — Overhang parity | **Confirmed fixed** | Arithmetic reconciled on both sides. Windows (`MainWindow.xaml.cs:47,643-648`): window left `= clockRight + 2`, window top `= clockTop − 8`; +4 inset ⇒ visible square at `clockRight + 6`, `clockTop − 4`. macOS (`TimerWindow.swift:1051-1060`): origin `x = maxX + 2`, `y = maxY + 4 + 4 − 30 = maxY − 22`; +4 inset ⇒ visible square left `= maxX + 6`, top edge `= maxY + 4`. **Both platforms: 6-unit side gap, 4-unit top overhang.** |
| Issue 3 — Process-wide capture release | **Confirmed fixed** | `PlaybackButtonWindow.xaml.cs:141,167` both use `ButtonSurface.ReleaseMouseCapture()`. No `Mouse.Capture(null)` remains in the file. |
| Issue 4 — macOS halo click target | **Confirmed fixed** | All six touch points now derive from one rectangle — see checklist below. |

### Issue 4 — detailed verification

`PlaybackButtonLayout` (`TimerWindow.swift:185-190`) defines `panelSize = 30`,
`surfaceInset = 4`, `gap = 2`, `topOverhang = 4`. `interactiveBounds`
(`:201-206`) is `bounds.insetBy(dx: 4, dy: 4)` ⇒ **`(4, 4, 22, 22)`** for the
30×30 view. Every consumer resolves to that same rectangle:

| Concern | Site | Uses `interactiveBounds`? |
|---|---|---|
| Hit testing | `:208-210` `hitTest` | ✅ returns `nil` outside |
| Hover tracking | `:212-226` `updateTrackingAreas` → `rect: interactiveBounds` | ✅ (replaces the implicit full-`bounds` rect) |
| Mouse-up containment | `:250-262` `interactiveBounds.contains(convert(event.locationInWindow, from: nil))` | ✅ |
| Drawing | `:314` `let square = interactiveBounds` (fill + 1.5 stroke) | ✅ |
| Panel/view sizing | `:355-365` both built from `PlaybackButtonLayout.panelSize` | ✅ |
| Positioning | `:1051-1060` derives from `gap` / `topOverhang` / `surfaceInset` | ✅ |

The four-point halo is therefore never filled or stroked (stroke extends only to
`3.25 … 26.75`, inside the 30×30 view), so it stays fully transparent and keeps
serving its original purpose — headroom for `panel.hasShadow = true` — while
`hitTest` refuses pointer interaction there. **Issue 4 is resolved as claimed.**

## Evidence Checklist

- [x] `dotnet build MiniStopwatch.sln -c Release -v minimal` → **Build succeeded,
  0 Warning(s), 0 Error(s)** on the current working tree (all five projects,
  including `MiniStopwatch.App` with the post-fix `PlaybackButtonWindow.xaml`).
- [x] `dotnet run --project MiniStopwatch.Tests -c Release --no-build` →
  **All 26 tests passed**, including
  `Playback control is blocked by active pause reasons`.
- [x] Re-derived the Issue 2 geometry from source constants on both platforms
  (table above) and confirmed the two visible 22-unit squares now sit at the same
  6-unit side gap and 4-unit top overhang relative to the clock.
- [x] Mapped all six Issue 4 touch points to `interactiveBounds`
  (`TimerWindow.swift:201-206, 208-210, 212-226, 250-262, 314, 355-365, 1051-1060`)
  and confirmed no residual use of raw `bounds` for interaction or drawing in
  `PlaybackButtonView`.
- [x] **Verified the macOS drag-off cancellation path independently.** AppKit
  routes drag/up events to the view that received `mouseDown`, so `mouseUp` still
  fires when the release happens outside the panel; `convert(event.locationInWindow, from: nil)`
  then yields a point outside `(4,4,22,22)` and `onToggle` is skipped
  (`TimerWindow.swift:250-262`). Correct.
- [x] **Verified the Windows drag-off cancellation path against the real WPF
  sources**, because the naive reading of `PlaybackButtonWindow.xaml.cs:159-171`
  looks unsafe. Under `CaptureMode.Element`, `MouseDevice` forces the mouse-over
  element to the capture element — `dotnet/wpf` `MouseDevice.cs`:
  `case CaptureMode.Element: … if (mouseOver != _mouseCapture) { /* Always consider the mouse over the capture point. */ mouseOver = _mouseCapture; isPhysicallyOver = false; }`
  — so `ButtonSurface.IsMouseOver` is unconditionally `true` while capture is
  held and `MouseLeave` cannot fire. (This is why WPF's own `ButtonBase` does not
  trust `IsMouseOver` under capture and instead recomputes via
  `UpdateIsPressed() { Point pos = Mouse.PrimaryDevice.GetPosition(this); … }`.)
  The handler is nevertheless **correct** only because it calls
  `ButtonSurface.ReleaseMouseCapture()` *before* testing `IsMouseOver`:
  `MouseDevice.ChangeMouseCapture` ends with `// Force a mouse move so we can update the mouse over. Synchronize();`,
  and `InputManager.ProcessInput` runs synchronously, so the mouse-over state is
  re-hit-tested against the real cursor position before the `if` executes.
  Press-then-drag-off-then-release correctly does **not** toggle tracking. Noted
  here rather than raised as a finding because it is presently correct, but the
  two statements at `:167` and `:170` must not be reordered.
- [x] Ruled out an HWND-recreation hazard from `Window_StateChanged` setting
  `ShowInTaskbar = false` while `playbackButtonWindow.Owner == this`.
  `MinimizeMenuItem_Click` genuinely toggles the property (`true` → minimize →
  `false` on restore), but `dotnet/wpf` `Window.OnShowInTaskbarChanged` only does
  `SWP_HIDEWINDOW` → style-bit update → `SWP_SHOWWINDOW`; it does **not**
  recreate the handle, so the owner relationship and the owned window survive the
  minimize/restore cycle. No finding.
- [x] Ruled out an exit-hang from the eagerly created, never-explicitly-closed
  `playbackButtonWindow`. Win32 `DestroyWindow` destroys owned windows *before*
  the owner, each owned HWND's `WM_DESTROY` drives WPF's
  `Window.InternalDispose() → UpdateWindowListsOnClose()`, and the collection
  therefore reaches zero under the default `ShutdownMode.OnLastWindowClose`.
  Matches the already-shipped `statsWindow` pattern. No finding.
- [x] Reconfirmed release metadata at 2.9.0 across `Directory.Build.props`,
  `VERSION`, `MiniStopwatch.App/app.manifest`, `browser-extension/manifest.json`,
  `README.md` and `docs/index.html`; `git --no-pager diff --stat` shows no stale
  2.8.1 reference and no unintended file in the change set.
- [x] Re-checked the new marketing markup for clipping: `.context-playback-button`
  (`docs/styles.css:865-887`) sits at `right: -30px; top: -10px` inside
  `.context-timer` (`position: absolute`, `:835`), whose ancestor `.context-demo`
  (`:825-833`) is `position: relative` with **no** `overflow` clip, so the
  protruding button renders. Being absolutely positioned it is not a flex item,
  so `.context-timer`'s `gap: 10px` is unaffected.
- [x] Re-checked documentation accuracy: `README.md:26-29`'s "disabled whenever a
  lock or Focus Protection pause reason is active, even if tracking was already
  paused manually" is exactly `IsPlaybackControlBlocked => automaticPauseReasons.Count > 0`
  (`StopwatchController.cs:27`), a strict superset of `IsAutomaticallyPaused`
  (`:24-25`). Confirmed the deliberate divergence from the context menu (which
  keeps using `IsAutomaticallyPaused`) is the documented design, not a defect:
  `Start()` under an active reason only arms `resumeAfterAutomaticPause`.
- [ ] Did not execute either GUI (no interactive WPF desktop session, no macOS
  host for `swiftc`). All geometry and event-routing conclusions above were
  derived from source constants plus the authoritative WPF/AppKit contracts
  cited inline.

## Issues

### Issue 5: macOS `hitTest` compares a superview-space point against view-space bounds
- **Severity**: Low
- **File**: `macos/Sources/TimerWindow.swift`
- **Line(s)**: 208-210
- **Description**: The Issue 4 fix reads

  ```swift
  override func hitTest(_ point: NSPoint) -> NSView? {
      interactiveBounds.contains(point) ? super.hitTest(point) : nil
  }
  ```

  `interactiveBounds` is derived from `self.bounds`, i.e. it is in the **view's
  own** coordinate space. But AppKit's `NSView.hitTest(_:)` documents its
  parameter as *"A point that is in the coordinate system of the view's
  superview, not of the view itself"* — the opposite of UIKit's
  `hitTest(_:with:)`. The two spaces are being mixed.

  This is **not a present defect**: `PlaybackButtonView` is the `contentView` of a
  `.borderless` panel, so its frame inside the window's frame view is
  `(0, 0, 30, 30)` and its `bounds` origin is `(0, 0)`. The superview→view
  transform is the identity, the comparison is numerically correct today, and
  halo points do return `nil` exactly as the Issue 4 resolution claims. The
  delegating `super.hitTest(point)` call is separately correct because AppKit's
  own implementation performs the conversion.
- **Risk**: Latent, not active. The guard silently becomes wrong the moment the
  view stops being a borderless content view at origin — e.g. if the button is
  ever embedded as a subview of the clock's content view (an obvious future
  simplification that would remove the second window entirely), or if the panel
  gains a non-zero content-view origin. The failure mode would be a click target
  offset by the view's frame origin, which is easy to misdiagnose because the
  drawing, the tracking area and the mouse-up check would all still be right.
- **Suggested Fix**: Convert into the receiver's space before the containment
  test, e.g.
  `let local = convert(point, from: superview)` then
  `interactiveBounds.contains(local) ? super.hitTest(point) : nil`.
  Behaviour is unchanged today and correct under any future transform.

## Resolution Log
_Updated by the driving agent as findings are addressed._

### Issue 5
- **Status**: Open
- **What changed**: pending
- **Why**: pending
- **How verified**: pending

## Notes for the driving agent

- Issue 5 is a hardening-only change with **zero** behavioural delta on the
  current window topology. It is safe to fix now or to defer past 2.9.0.
- This artifact is still **untracked**. Per the round-6 instructions it must be
  `git add`-ed and committed together with the reviewed code.
- No source file was modified during this re-review.

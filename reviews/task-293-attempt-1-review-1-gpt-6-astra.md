# Review Round 1 — Broad sweep

**Model:** `gpt-6-astra`
**Release:** Productivity Tracker 2.10.0

## Findings

### 1. macOS cannot compile the new activity type

- **Severity:** High
- **File:** `macos/Sources/FocusProtection.swift`
- **Finding:** `BrowserActivityKind` was nested inside `FocusProtectionPaths`,
  while `FocusSocketServer` and `TimerWindowController` referenced it as a
  module-level type.
- **Resolution:** Moved `BrowserActivityKind` to module scope so every macOS
  consumer resolves the same type.

### 2. Dock sizing mixes DPI coordinate systems

- **Severity:** Medium
- **Files:** `MiniStopwatch.App/MainWindow.xaml.cs`,
  `MiniStopwatch.App/app.manifest`
- **Finding:** Dock placement used physical taskbar coordinates and per-monitor
  DPI while the application did not explicitly opt into per-monitor DPI
  awareness.
- **Resolution:** Enabled PerMonitorV2 DPI awareness so the WPF window and
  taskbar placement calculations use compatible monitor-specific scaling.

### 3. Window titles override authoritative non-YouTube classifications

- **Severity:** Medium
- **Files:** `MiniStopwatch.App/MainWindow.xaml.cs`,
  `MiniStopwatch.Core/BrowserPausePolicy.cs`
- **Finding:** A title containing ` - YouTube` could replace an explicit
  `OtherDistracting` classification.
- **Resolution:** Restricted the foreground-title compatibility fallback to
  `UnknownDistracting` signals and added policy tests proving explicit
  non-YouTube classifications remain authoritative.

### 4. Older native-host acknowledgements trigger an endless resend loop

- **Severity:** Medium
- **Files:** `browser-extension/background.js`,
  `browser-extension-tests/background.test.js`
- **Finding:** Boolean-only acknowledgements from an older native host were
  normalized to `other`, causing repeated YouTube category resends.
- **Resolution:** Treat boolean-only acknowledgements as successful delivery
  without inferring a category, and added a bounded-message regression test.

### 5. macOS exempted unknown legacy activity

- **Severity:** Medium
- **File:** `macos/Sources/TimerWindow.swift`
- **Resolution:** Continue pausing for both `otherDistracting` and
  `unknownDistracting`; only explicit YouTube activity is exempt.

### 6. Mixed-DPI undocking restored incorrect coordinates and size

- **Severity:** Medium
- **File:** `MiniStopwatch.App/MainWindow.xaml.cs`
- **Resolution:** Preserve physical pre-dock bounds, clamp them to a visible
  monitor, move the HWND to the destination monitor first, and then apply its
  saved physical size after the DPI transition.

### 7. Delayed legacy acknowledgements could miss reconciliation

- **Severity:** Medium
- **Files:** `browser-extension/background.js`,
  `browser-extension-tests/background.test.js`
- **Resolution:** Compare the acknowledged active boolean with current state,
  resend only on a boolean mismatch, and test delayed stale acknowledgements.

### 8. Foreground process exit could terminate the UI timer

- **Severity:** Medium
- **File:** `MiniStopwatch.App/MainWindow.xaml.cs`
- **Resolution:** Treat both missing process IDs and exited process objects as
  a non-YouTube foreground result.

### 9. Stats popup retained stale monitor DPI

- **Severity:** Medium
- **Files:** `MiniStopwatch.App/MainWindow.xaml.cs`,
  `MiniStopwatch.App/StatsWindow.xaml.cs`
- **Resolution:** Calculate stats layout in physical monitor coordinates and
  move then resize the popup HWND in two steps across DPI boundaries.

### 10. Vertical taskbar detection used desktop origin

- **Severity:** Medium
- **File:** `MiniStopwatch.App/MainWindow.xaml.cs`
- **Resolution:** Determine the vertical taskbar edge relative to its owning
  monitor instead of coordinate zero.

## Verification

- 33 application tests passed.
- Both browser-extension test suites passed.
- Windows release build completed with zero warnings and zero errors.
- Final round-1 rerun: **CLEAN**.

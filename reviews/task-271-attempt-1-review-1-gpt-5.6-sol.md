## Review Summary
- **Round**: 1
- **Theme**: Broad sweep
- **Mode**: sequential
- **Model**: gpt-5.6-sol
- **Artifact**: `reviews/task-271-attempt-1-review-1-gpt-5.6-sol.md`
- **Issues Found**: 2
- **Verdict**: ISSUES_FOUND

## Evidence Checklist
- [x] Reviewed the complete uncommitted diff and surrounding Windows and macOS widget lifecycle code.
- [x] Built the Windows solution and ran all 25 tests.
- [x] Inspected the built Windows stats-window native activation behavior.
- [x] Compared all release-version metadata with the previous release.
- [ ] macOS compilation is delegated to the GitHub Actions macOS runners because AppKit is unavailable on Windows.

## Issues

### Issue 1: Clicking inside the Windows report dismisses it
- **Severity**: Medium
- **File**: `MiniStopwatch.App/MainWindow.xaml`, `MiniStopwatch.App/MainWindow.xaml.cs`, `MiniStopwatch.App/StatsWindow.xaml`
- **Line(s)**: Main window deactivation handling and stats-window activation settings
- **Description**: `ShowActivated="False"` prevents activation when initially shown but does not apply the native `WS_EX_NOACTIVATE` style. Clicking the report can therefore deactivate its owner and trigger dismissal even though the click is inside the report.
- **Risk**: The report closes on an inside click, contrary to the requested outside-click behavior.
- **Suggested Fix**: Apply `WS_EX_NOACTIVATE` to the stats-window HWND while retaining owner deactivation for outside clicks.

### Issue 2: The 2.7.1 release version is only partially propagated
- **Severity**: Medium
- **File**: `MiniStopwatch.App/app.manifest`, `MiniStopwatch.Installer/Program.cs`, `browser-extension/manifest.json`
- **Line(s)**: Version declarations
- **Description**: The canonical version and download links were updated, but the Windows application manifest, installed-app display version, and browser extension remained at 2.7.0.
- **Risk**: Shipped components identify themselves inconsistently and installed-version reporting is incorrect.
- **Suggested Fix**: Update every release metadata field to 2.7.1.

## Resolution Log

### Issue 1
- **Status**: Fixed
- **What changed**: `StatsWindow` now applies `WS_EX_NOACTIVATE` and explicitly returns `MA_NOACTIVATE` for `WM_MOUSEACTIVATE`; the native hook is removed when the window closes.
- **Why**: Runtime re-review showed that the extended style alone was insufficient for WPF. Handling the mouse-activation message keeps the owner active when the report itself is clicked while owner deactivation still identifies outside clicks.
- **How verified**: Rebuilt the Windows package and reran the complete Windows test suite after the fix.

### Issue 2
- **Status**: Fixed
- **What changed**: Updated the application manifest, installer `DisplayVersion`, and browser-extension manifest to 2.7.1.
- **Why**: Every distributed component must report the same release version.
- **How verified**: Searched release metadata for stale 2.7.0 identifiers and rebuilt the Windows package.

## Re-review
- **Issues Found**: 0
- **Verdict**: CLEAN
- **Evidence**: A Win32 interaction smoke test confirmed that clicking inside the report keeps it visible and clicking outside hides it. Version metadata is consistent, the Windows build is clean, all 25 tests pass, and both browser-extension test suites pass.

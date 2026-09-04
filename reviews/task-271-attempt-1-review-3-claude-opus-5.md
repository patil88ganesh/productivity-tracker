## Review Summary
- **Round**: 3
- **Theme**: Edge cases & robustness
- **Mode**: sequential
- **Model**: claude-opus-5
- **Artifact**: `reviews/task-271-attempt-1-review-3-claude-opus-5.md`
- **Issues Found**: 4
- **Verdict**: ISSUES_FOUND

## Evidence Checklist
- [x] Reviewed the complete uncommitted diff and surrounding Windows and macOS lifecycle code.
- [x] Built the Windows solution and ran all 25 tests.
- [x] Ran both browser-extension test suites.
- [x] Verified WPF context-menu input ordering and the Windows native activation constants.
- [x] Verified release-version metadata consistency.
- [ ] macOS compilation and runtime validation require the GitHub Actions macOS runners.

## Issues

### Issue 1: Windows right-click dismisses the report before the context menu action
- **Severity**: Medium
- **File**: `MiniStopwatch.App/MainWindow.xaml.cs`
- **Description**: The outside-click behavior runs before the context menu opens, so the old menu toggle branch cannot close a still-visible report.
- **Risk**: The previous toggle wording and restore logic no longer match the new outside-click requirement.
- **Suggested Fix**: Define the menu action as opening the report and outside clicks as the only dismissal behavior.

### Issue 2: macOS right-click has the same pre-menu dismissal ordering
- **Severity**: Medium
- **File**: `macos/Sources/StatsWidget.swift`, `macos/Sources/TimerWindow.swift`
- **Description**: The local right-mouse monitor dismisses the report before the context menu action.
- **Risk**: Toggle state and minimization restoration conflict with the new outside-click semantics.
- **Suggested Fix**: Make the menu action explicitly open the report and dismiss it on all outside interactions.

### Issue 3: Windows minimize restoration conflicts with outside-click dismissal
- **Severity**: Low
- **File**: `MiniStopwatch.App/MainWindow.xaml.cs`
- **Description**: Opening the Minimize menu is itself an outside click, so retaining old restore state is unreachable and misleading.
- **Risk**: Dead state implies behavior the new requirement intentionally replaced.
- **Suggested Fix**: Remove the restore state and keep the report dismissed after minimization.

### Issue 4: macOS app hiding leaves click monitors active
- **Severity**: Low
- **File**: `macos/Sources/StatsWidget.swift`, `macos/Sources/TimerWindow.swift`, `macos/Sources/AppDelegate.swift`
- **Description**: Hiding the application can leave monitors installed while the report is not visible.
- **Risk**: A later global click silently changes report state and monitoring remains active unnecessarily.
- **Suggested Fix**: Dismiss the report when the application hides.

## Resolution Log

### Issue 1
- **Status**: Fixed
- **What changed**: Removed the obsolete Windows toggle branch and reasserted `HideStatsWindow()` when the owner returns to `WindowState.Normal`.
- **Why**: This directly models the requested behavior and prevents Windows from restoring an owned report after externally initiated minimization.
- **How verified**: Windows package rebuild, interaction smoke testing, and review of both internal and external minimize paths.

### Issue 2
- **Status**: Fixed
- **What changed**: Replaced the macOS toggle action with an explicit open action, moved app-hide dismissal to `applicationWillHide`, and made dismissal idempotently order the panel out.
- **Why**: This preserves strict outside-click dismissal and removes the panel before AppKit records windows for unhide restoration.
- **How verified**: Static lifecycle review followed by macOS CI compilation.

### Issue 3
- **Status**: Fixed
- **What changed**: Removed Windows and macOS report restoration after minimization.
- **Why**: Choosing Minimize occurs outside the report and must leave it closed.
- **How verified**: Reviewed all minimize and restore paths and rebuilt Windows.

### Issue 4
- **Status**: Fixed
- **What changed**: `AppDelegate.applicationWillHide` now dismisses the report before application hiding, which also removes both mouse monitors.
- **Why**: Hidden UI should not retain global input monitoring.
- **How verified**: Reviewed hide, termination, minimization, and explicit dismissal paths.

### Re-review issue: Unchecked native style calls
- **Status**: Fixed
- **What changed**: Native style reads and writes now use `SetLastError`, clear and inspect the P/Invoke error, throw explicit `Win32Exception`s on failure, and require a valid `HwndSource`.
- **Why**: A failed style read must not silently overwrite required layered and topmost extended styles.
- **How verified**: Rebuilt the Windows package with zero warnings and errors.

## Re-review
- **Issues Found**: 0
- **Verdict**: CLEAN
- **Evidence**: The final lifecycle paths, native error handling, version metadata, Windows build, all 25 Windows tests, and both browser-extension test suites were rechecked after the fixes.

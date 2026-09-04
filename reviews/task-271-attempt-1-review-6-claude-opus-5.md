## Review Summary
- **Round**: 6
- **Theme**: Polish & hardening
- **Mode**: sequential
- **Model**: claude-opus-5
- **Artifact**: `reviews/task-271-attempt-1-review-6-claude-opus-5.md`
- **Issues Found**: 3
- **Verdict**: ISSUES_FOUND

## Evidence Checklist
- [x] Reviewed the full working-tree diff and all window lifecycle changes.
- [x] Built Windows, ran all 25 tests, and ran both browser-extension suites.
- [x] Verified release metadata and packaged binary versions.
- [x] Verified Win32 constants, native error semantics, and monitor cleanup.
- [x] Checked user-facing documentation against the implemented open-and-dismiss behavior.
- [ ] macOS compilation and runtime validation require the GitHub Actions macOS runners.

## Issues

### Issue 1: Hidden report is repositioned on every tracker move and resize
- **Severity**: Low
- **File**: `MiniStopwatch.App/MainWindow.xaml.cs`
- **Description**: Move and resize events continue calculating monitor geometry and updating a report that the initiating outside click already hid.
- **Risk**: Unnecessary UI-thread work during latency-sensitive dragging and resizing.
- **Suggested Fix**: Skip positioning hidden reports except immediately before opening one.

### Issue 2: Optional native style failure terminates the tracker
- **Severity**: Low
- **File**: `MiniStopwatch.App/StatsWindow.xaml.cs`
- **Description**: A checked user32 style failure throws from the UI event path even though the `WM_MOUSEACTIVATE` hook already preserves core behavior.
- **Risk**: A cosmetic failure can terminate the timer and lose unsaved active-time data.
- **Suggested Fix**: Trace the specific native error and continue with the essential message hook.

### Issue 3: Installer display version is duplicated manually
- **Severity**: Low
- **File**: `MiniStopwatch.Installer/Program.cs`
- **Description**: `DisplayVersion` is a hand-maintained literal despite the installer assembly already carrying the canonical version.
- **Risk**: Future releases can register an incorrect installed version without failing a build.
- **Suggested Fix**: Derive the registry value from the installer assembly version.

## Resolution Log

### Issue 1
- **Status**: Fixed
- **What changed**: `PositionStatsWindow` now skips hidden reports by default and accepts `includeHidden: true` only for the pre-show positioning path.
- **Why**: The report still opens at the correct location without doing hidden-window work throughout drag and resize gestures.
- **How verified**: Rebuilt Windows and traced every positioning caller.

### Issue 2
- **Status**: Fixed
- **What changed**: Native style failures now emit specific `Trace.TraceWarning` diagnostics and return without throwing; the essential `WM_MOUSEACTIVATE` hook remains installed.
- **Why**: The optional extended style should degrade safely without terminating active tracking.
- **How verified**: Rebuilt Windows with zero warnings and reran the complete test suite.

### Issue 3
- **Status**: Fixed
- **What changed**: Installer `DisplayVersion` is now derived from `typeof(Program).Assembly.GetName().Version`.
- **Why**: The assembly version is generated from the shared canonical version and cannot drift independently.
- **How verified**: Rebuilt the installer and inspected its version metadata.

## Re-review
- **Issues Found**: 0
- **Verdict**: CLEAN
- **Evidence**: Every code finding from rounds 1 through 6 is resolved. Windows builds without warnings, all 25 Windows tests pass, both browser-extension suites pass, release metadata is consistent, and the installer derives version 2.7.1 from its assembly.

### Release-validation gate: macOS sources require native compilation
- **Severity**: High (release blocker)
- **File**: `macos/Sources/AppDelegate.swift`, `macos/Sources/StatsWidget.swift`, `macos/Sources/TimerWindow.swift`
- **Description**: AppKit is unavailable on the Windows development host, so the changed Swift files require the repository's arm64 and Intel GitHub Actions runners.
- **Risk**: Tagging before both native builds pass could publish an uncompilable macOS release.
- **Suggested Fix**: Push the reviewed commit, require both macOS matrix jobs to pass, then record that evidence here before tagging v2.7.1.

## Resolution Log (re-review)

### Release-validation gate
- **Status**: Fixed
- **What changed**: GitHub Actions run `33915095120` compiled and packaged both macOS architectures from commit `98694bf5ab27fc0af733974aae1b38029080cec9`.
- **Why**: The macOS workflow runs only after the changed sources are pushed.
- **How verified**: The `macos-15` arm64 job and `macos-15-intel` x64 job both completed successfully and produced release artifacts.

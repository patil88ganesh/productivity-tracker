# Review Round 6 — Polish and hardening

**Model:** `claude-opus-5.5`
**Release:** Productivity Tracker 2.10.0

## Findings and resolutions

### 1. Missing Widgets preference was interpreted as disabled

- **Severity:** Medium
- **Resolution:** On Windows 11, a missing `TaskbarDa` value now uses the
  operating-system default only when Widgets is not disabled by policy and the
  Windows Web Experience package is installed.

### 2. Widgets policy handling was incomplete and ordered incorrectly

- **Severity:** Medium
- **Resolution:** `AllowNewsAndInterests=0` and
  `DisableWidgetsBoard!=0` are now evaluated before the per-user `TaskbarDa`
  preference so administrator policy always wins.

### 3. Browser-extension privacy documentation described the legacy protocol

- **Severity:** Low
- **Resolution:** Updated the bundled extension README to document the
  inactive, YouTube, and other-selected-site categories and explicitly state
  that URLs, page content, and browsing history are not transmitted.

### 4. Swift `.none` assignments were ambiguous

- **Severity:** Low
- **Resolution:** Replaced ambiguous assignments with explicit
  `BrowserActivityKind.none` values.

## Verification

- 33 application tests passed.
- Both browser-extension test suites passed.
- Release build completed with zero warnings and zero errors.
- `git diff --check` passed.
- Final round-6 rerun: **CLEAN**.

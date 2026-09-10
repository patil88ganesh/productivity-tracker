# Round 6 — Polish & hardening

**Model:** `claude-opus-5`  
**Task:** Move the Play/Pause control inside the miniwatch  
**Result:** Two issues found and resolved.

## Evidence checklist

- Reviewed all production, test, version, documentation, and website changes.
- Confirmed no source reference remains to the deleted companion windows or
  playback-placement API.
- Confirmed version 2.9.2 is consistent across product manifests and public
  download documentation.
- Checked WPF dependency-property precedence for disabled tooltips and cursors.
- Checked cross-platform icon geometry, sizing, state colors, and tooltips.

## Finding 1 — Disabled tooltip was suppressed on Windows

**Severity:** Medium  
**File:** `MiniStopwatch.App/MainWindow.xaml`

WPF does not show a tooltip for a disabled element unless
`ToolTipService.ShowOnDisabled` is enabled, making the automatic-pause
explanation unreachable.

### Resolution

Added `ToolTipService.ShowOnDisabled="True"` to the inline button so the blocked
state continues to explain why playback is unavailable.

## Finding 2 — Disabled cursor trigger could not override a local value

**Severity:** Low  
**File:** `MiniStopwatch.App/MainWindow.xaml`

The button's local `Cursor="Hand"` value had higher dependency-property
precedence than the control-template trigger's `Cursor="Arrow"` setter.

### Resolution

Moved the hand cursor to the template's `PlaybackSurface` border and targeted
the disabled arrow setter at that same element. The enabled and disabled cursor
states now apply at the same precedence level.

## Verification

The release build, timer test harness, browser-extension tests, and diff checks
are rerun after these corrections. The round is rerun against the corrected
diff before publication.

## Re-review

The corrected diff was re-reviewed. No additional high-confidence actionable
finding remained, and the final polish round passed cleanly.

## macOS release-build evidence

- Workflow: `Build macOS`
- Run: `34443581105`
- Source commit: `f719bb765c5950df72bbb632470027eb7e832f29`
- Conclusion: `success`
- Apple silicon artifact: `10138815265`
  - SHA-256: `e38b8fc429dc70a67f00eb58a4e41d908c68fc190871829a859ec28e034830d9`
- Intel artifact: `10138827046`
  - SHA-256: `974145c48a4c7e6441ec8fdd56b2f224ae34340d4eb79847320ea756281be7bc`

# Round 4 — Detailed correctness

**Model:** `gpt-6-astra`  
**Task:** Move the Play/Pause control inside the miniwatch  
**Result:** One medium-severity issue found and resolved.

## Evidence checklist

- Reviewed the inline control geometry and timer-text layout at the supported
  minimum, default, and enlarged window sizes.
- Traced display formatting across the two-digit to three-digit hour boundary.
- Checked the resize and timer-refresh paths that recalculate font size.

## Finding 1 — Three-digit hours clipped at minimum width

**File:** `MiniStopwatch.App/MainWindow.xaml.cs`  
**Severity:** Medium

The existing 20 px minimum font size fit `100:00:00` before internal space was
reserved for the inline button, but could clip the nine-character display at
the supported 140 px minimum window width.

### Resolution

`ScaleDisplay()` now measures the current formatted time with the actual WPF
typeface and the timer grid's actual text-column width. It proportionally
reduces the font, down to a conservative 12 px floor, only when the formatted
display would otherwise clip. A transition across an hour-digit boundary
triggers a new scale calculation, while normal 100 ms timer updates avoid
repeated text measurement.

An initial fix incorrectly used `TimeDisplay.ActualWidth`. Because the text
element is center-aligned, that value represented the previously rendered text
rather than the available grid column and could prevent enlargement or retain
clipping after a shrink. The corrected implementation uses column 1's
`ActualWidth`, with a pre-layout fallback derived from border, padding, status,
and inline-control widths.

The second re-review found that the default `FormattedText` overload measures
with ideal glyph metrics while the timer explicitly renders in display mode.
Measurement now uses `TextFormattingMode.Display` and remeasures up to three
times with a small safety margin, so device-pixel rounding cannot leave the
fitted text clipped.

The final re-review found no additional significant issue.

### Verification

The release build and timer test harness were rerun after the correction. The
review round is rerun against the corrected diff before advancing.

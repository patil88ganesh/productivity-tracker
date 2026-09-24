# Review Round 3 — Edge cases and robustness

**Model:** `claude-opus-5.5`
**Release:** Productivity Tracker 2.10.0

## Findings and resolutions

### 1. Left-edge taskbar controls could be covered

- **Severity:** Medium
- **Resolution:** Dock inside the taskbar only when the far-left slot is clear;
  otherwise use the far-left adjacent fallback.

### 2. Auto-hide taskbar geometry could move the dock off-screen

- **Severity:** Medium
- **Resolution:** Use the stable `ABM_GETTASKBARPOS` rectangle for auto-hide
  taskbars and clamp final bounds to the owning monitor.

### 3. Hidden taskbar HWND could resolve to a neighboring monitor

- **Severity:** Medium
- **Resolution:** Select the monitor and effective DPI from the stable taskbar
  rectangle rather than the temporarily slid-off HWND bounds.

## Verification

- 33 application tests passed.
- Both browser-extension test suites passed.
- Release build completed with zero warnings and zero errors.
- Final round-3 rerun: **CLEAN**.

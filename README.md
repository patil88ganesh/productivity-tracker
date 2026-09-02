# Productivity Tracker

A compact, semi-transparent, always-on-top productivity time tracker for Windows.

## Controls

- Middle-click the timer to start or stop.
- Left-drag the timer to move it.
- Right-click for Start/Stop, Reset, Transparency, Minimize, and Exit.
- Transparency can be set to 40%, 55%, 70%, 85%, or 100% and is remembered across restarts.
- Minimize sends the stopwatch to the Windows taskbar. Restore it from the taskbar to return to the compact overlay.
- When Windows locks, a running stopwatch pauses. It automatically resumes after unlock without counting the locked time.

## Install

Run `dist\ProductivityTracker-Setup.exe`. The per-user installer does not require administrator rights and creates Desktop and Start Menu shortcuts.

## Build

From PowerShell:

```powershell
.\build.ps1
```

The build requires the .NET 8 SDK and produces:

- `dist\ProductivityTracker-Setup.exe`
- `dist\portable\` (an unpacked portable build; launch `ProductivityTracker.exe`)

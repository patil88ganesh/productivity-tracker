# Productivity Tracker

A compact, semi-transparent, always-on-top productivity time tracker for Windows.

## Website

Visit the [Productivity Tracker website](https://patil88ganesh.github.io/productivity-tracker/) for features, controls, and downloads.

## Controls

- Middle-click the timer to start or stop.
- Left-drag the timer to move it.
- Drag any edge or corner to resize it. The digits scale automatically and the selected size is remembered.
- Hovering over the clock highlights its surface, border, and shadow.
- Right-click for Start/Stop, Reset, Add and Start, Set Timer, Transparency, Minimize, and Exit.
- Add and Start accepts hours and minutes, adds them to the current stopwatch total, and immediately resumes counting.
- Set Timer opens a custom hours/minutes/seconds countdown. Middle-click pauses or resumes it.
- When a countdown reaches zero, the app plays a sound and flashes visually and on the taskbar.
- Exit Timer returns to regular stopwatch mode.
- Transparency can be set to 40%, 55%, 70%, 85%, or 100% and is remembered across restarts.
- Minimize sends the stopwatch to the Windows taskbar. Restore it from the taskbar to return to the compact overlay.
- When Windows locks, a running stopwatch pauses. It automatically resumes after unlock without counting the locked time.
- Optional Focus Protection pauses a running stopwatch or countdown while the focused Edge or Chrome tab is on a supported social-media site or WhatsApp Web.

## Focus Protection

Focus Protection is disabled by default and requires the bundled browser extension:

1. Install Productivity Tracker v2.4.0 or later.
2. Right-click the clock and select **Focus Protection > Browser Extension Setup**.
3. Open `edge://extensions` or `chrome://extensions`.
4. Enable Developer mode and select **Load unpacked**.
5. Select the opened `browser-extension` folder.
6. Enable **Focus Protection > Pause on social media and WhatsApp**.

Supported domains are Facebook, Instagram, X/Twitter, Reddit, LinkedIn, YouTube, TikTok, and WhatsApp Web. The extension checks the active domain locally and sends only an active/inactive signal to the desktop app; it does not send URLs or browsing history.

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

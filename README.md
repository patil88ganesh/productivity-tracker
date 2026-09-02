# Productivity Tracker

Productivity Tracker is a compact, semi-transparent, always-on-top stopwatch
and countdown timer for Windows and macOS.

## Downloads — version 2.5.0

- **Windows 10/11 x64:** [ProductivityTracker-Setup.exe](https://github.com/patil88ganesh/productivity-tracker/releases/download/v2.5.0/ProductivityTracker-Setup.exe)
- **macOS 12+ Apple silicon:** [ProductivityTracker-macOS-arm64.app.zip](https://github.com/patil88ganesh/productivity-tracker/releases/download/v2.5.0/ProductivityTracker-macOS-arm64.app.zip)
- **macOS 12+ Intel:** [ProductivityTracker-macOS-x64.app.zip](https://github.com/patil88ganesh/productivity-tracker/releases/download/v2.5.0/ProductivityTracker-macOS-x64.app.zip)

Release downloads will be published under:

`https://github.com/patil88ganesh/productivity-tracker/releases/tag/v2.5.0`

The macOS build is a beta with an ad-hoc signature and is not notarized. After
extracting the zip, move `ProductivityTracker.app` to Applications and try to
open it once. On macOS 15 or later, open **System Settings > Privacy & Security**,
click **Open Anyway** for Productivity Tracker, then confirm **Open**. On older
macOS versions, Control-click or right-click the app, choose **Open**, then
confirm **Open**. Do not bypass Gatekeeper globally.

## Features and controls

- Middle-click the timer to start, stop, pause, or resume.
- Left-drag the timer to reposition it.
- Drag any edge or corner to resize it; the digits scale automatically.
- Hover over the clock for a highlighted surface, border, and shadow.
- Right-click for Start/Stop, Reset, Add and Start, Set Timer, opacity,
  Minimize, Focus Protection, and Exit.
- Add and Start accepts hours and minutes, adds them to the current stopwatch
  total, and immediately resumes counting.
- Set Timer accepts hours, minutes, and seconds and starts a countdown.
- Countdown completion plays a sound and flashes the display and app icon.
- Opacity options are 40%, 55%, 70%, 85%, and 100%.
- Window size, position, opacity, and Focus Protection preference persist.
- A running stopwatch or countdown pauses while the user session is locked and
  resumes without counting locked time.
- Session lock and Focus Protection are independent, overlapping automatic
  pause reasons. Tracking resumes only after every active reason clears.

## Focus Protection

Focus Protection is optional and disabled by default. It supports Facebook,
Instagram, X/Twitter, Reddit, LinkedIn, YouTube, TikTok, and WhatsApp Web in
Google Chrome and Microsoft Edge.

1. Right-click the clock.
2. Choose **Focus Protection > Browser Extension Setup**.
3. Open `chrome://extensions` or `edge://extensions`.
4. Enable Developer mode and choose **Load unpacked**.
5. Select the folder opened by Productivity Tracker.
6. Enable **Pause on social media and WhatsApp** in the timer menu.

On Windows, setup registers the bundled .NET native messaging host. On macOS,
setup copies the extension into the user's Application Support folder and
registers the bundled Swift native host for Chrome and Edge. The macOS host
uses a user-only local Unix domain socket to communicate with the app.

The extension evaluates supported domains locally and sends only a boolean
active/inactive state. It does not send URLs, page content, or browsing history.

## Build Windows

Requirements: .NET 8 SDK on Windows.

```powershell
.\build.ps1
```

Outputs:

- `dist\ProductivityTracker-Setup.exe`
- `dist\portable\ProductivityTracker.exe`

## Build macOS

Requirements: macOS 12 or later with Xcode command-line tools.

```bash
bash ./macos/build-package.sh all
```

Use `arm64` or `x64` instead of `all` for one architecture. Outputs:

- `dist/macos/ProductivityTracker-macOS-arm64.app.zip`
- `dist/macos/ProductivityTracker-macOS-x64.app.zip`

The script uses only Apple command-line tools. It compiles the AppKit app and
native host, embeds the shared browser extension, applies an ad-hoc signature,
verifies the bundle, and creates release-ready zip archives. See
[`macos/README.md`](macos/README.md) for macOS-specific details.

## Website

Visit the [Productivity Tracker website](https://patil88ganesh.github.io/productivity-tracker/)
for features, controls, installation guidance, and platform downloads.

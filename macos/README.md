# Productivity Tracker for macOS

The macOS port is a dependency-free Swift/AppKit application with feature parity
with the Windows timer. It supports macOS 12 or later on Apple silicon and Intel.

## Build and package

Run on macOS with Xcode command-line tools installed:

```bash
bash ./macos/build-package.sh all
```

You can build one architecture with `arm64` or `x64`. The script compiles the
AppKit app and Swift native-messaging host, copies the Chromium extension into
the app bundle, applies an ad-hoc signature, verifies it, and creates:

- `dist/macos/ProductivityTracker-macOS-arm64.app.zip`
- `dist/macos/ProductivityTracker-macOS-x64.app.zip`

This beta is not Developer ID signed or notarized. After extracting it, move
`ProductivityTracker.app` to Applications and try to open it once. On macOS 15
or later, open **System Settings > Privacy & Security**, click **Open Anyway**
for Productivity Tracker, then confirm **Open**. On older macOS versions,
Control-click or right-click the app, choose **Open**, then confirm **Open**.
Do not bypass Gatekeeper globally.

## Focus Protection

Right-click the timer and choose **Focus Protection > Browser Extension
Setup…**. The app copies the bundled unpacked extension to:

`~/Library/Application Support/ProductivityTracker/browser-extension`

It also registers the native host manifests for Google Chrome and Microsoft
Edge in their user Application Support folders, opens the extension in Finder,
and copies its path. Enable Developer mode on `chrome://extensions` or
`edge://extensions`, choose **Load unpacked**, and select that folder.

After installing an app update, select **Reload** on the unpacked extension's
card to activate the latest domain list and connection fixes.

The extension sends an active/inactive state plus a random opaque visit token.
The native host forwards them over a user-only Unix domain socket. The token
recognizes the same protected visit without exposing domains, URLs, page
content, or browsing history outside the browser.

## My stats (mini)

Choose **My stats (mini)** from the timer's right-click menu to open the
attached seven-day report. It lists today and the previous six local calendar
days as `Date | Day | Hours`. Only active running time is counted; paused,
locked, Focus Protection, and app-closed time are excluded. Days with no active
tracked time display `NA`. The report closes automatically when you click
outside it.

## Continue counting

When Focus Protection pauses on a selected website that you are using for
work-related content, right-click the timer and choose **Continue counting**.
The timer resumes and records that protected tab and site visit as productive
time. Briefly clicking the tracker and returning to the same visit within 30
seconds keeps counting. Switching protected tabs or sites, visiting an
unprotected page, or returning after more than 30 seconds ends the override.
Choose **Stop counting this site** to cancel it immediately. Session-lock
pausing always remains enforced. Manual timer pauses, resumes, and timer-mode
changes do not end the current visit override.

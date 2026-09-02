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

The extension sends only a boolean active/inactive state. The native host
forwards it over a user-only Unix domain socket; URLs and browsing history never
leave the browser.

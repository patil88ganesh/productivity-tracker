#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"
OUTPUT="$ROOT/dist/macos"
REQUESTED_ARCH="${1:-all}"

case "$REQUESTED_ARCH" in
  all) ARCHITECTURES=("arm64" "x86_64") ;;
  arm64) ARCHITECTURES=("arm64") ;;
  x64|x86_64) ARCHITECTURES=("x86_64") ;;
  *)
    echo "Usage: $0 [all|arm64|x64|x86_64]" >&2
    exit 2
    ;;
esac

mkdir -p "$OUTPUT"

build_architecture() {
  local architecture="$1"
  local asset_architecture="$architecture"
  if [[ "$architecture" == "x86_64" ]]; then
    asset_architecture="x64"
  fi

  local work="$ROOT/.macos-build/$architecture"
  local app="$work/ProductivityTracker.app"
  local contents="$app/Contents"
  rm -rf "$work"
  mkdir -p "$contents/MacOS" "$contents/Resources"

  swiftc \
    -O \
    -whole-module-optimization \
    -target "$architecture-apple-macosx12.0" \
    "$ROOT/macos/Sources/TimerEngine.swift" \
    "$ROOT/macos/Sources/DailyStats.swift" \
    "$ROOT/macos/Sources/StatsWidget.swift" \
    "$ROOT/macos/Sources/FocusProtection.swift" \
    "$ROOT/macos/Sources/TimerWindow.swift" \
    "$ROOT/macos/Sources/AppDelegate.swift" \
    "$ROOT/macos/Sources/main.swift" \
    -framework AppKit \
    -o "$contents/MacOS/ProductivityTracker"

  swiftc \
    -O \
    -whole-module-optimization \
    -target "$architecture-apple-macosx12.0" \
    "$ROOT/macos/NativeHost/NativeMessagingHost.swift" \
    -o "$contents/MacOS/ProductivityTrackerNativeHost"

  cp -R "$ROOT/browser-extension" "$contents/Resources/browser-extension"
  cp "$ROOT/docs/assets/productivity-tracker.png" "$contents/Resources/ProductivityTracker.png"

  local iconset="$work/ProductivityTracker.iconset"
  mkdir -p "$iconset"
  sips -z 16 16 "$ROOT/docs/assets/productivity-tracker.png" --out "$iconset/icon_16x16.png" >/dev/null
  sips -z 32 32 "$ROOT/docs/assets/productivity-tracker.png" --out "$iconset/icon_16x16@2x.png" >/dev/null
  sips -z 32 32 "$ROOT/docs/assets/productivity-tracker.png" --out "$iconset/icon_32x32.png" >/dev/null
  sips -z 64 64 "$ROOT/docs/assets/productivity-tracker.png" --out "$iconset/icon_32x32@2x.png" >/dev/null
  sips -z 128 128 "$ROOT/docs/assets/productivity-tracker.png" --out "$iconset/icon_128x128.png" >/dev/null
  sips -z 256 256 "$ROOT/docs/assets/productivity-tracker.png" --out "$iconset/icon_128x128@2x.png" >/dev/null
  sips -z 256 256 "$ROOT/docs/assets/productivity-tracker.png" --out "$iconset/icon_256x256.png" >/dev/null
  sips -z 512 512 "$ROOT/docs/assets/productivity-tracker.png" --out "$iconset/icon_256x256@2x.png" >/dev/null
  sips -z 512 512 "$ROOT/docs/assets/productivity-tracker.png" --out "$iconset/icon_512x512.png" >/dev/null
  sips -z 1024 1024 "$ROOT/docs/assets/productivity-tracker.png" --out "$iconset/icon_512x512@2x.png" >/dev/null
  iconutil -c icns "$iconset" -o "$contents/Resources/ProductivityTracker.icns"

  cat > "$contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>ProductivityTracker</string>
  <key>CFBundleIdentifier</key>
  <string>com.patil88ganesh.productivity-tracker</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleIconFile</key>
  <string>ProductivityTracker</string>
  <key>CFBundleName</key>
  <string>Productivity Tracker</string>
  <key>CFBundleDisplayName</key>
  <string>Productivity Tracker</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
  <key>CFBundleVersion</key>
  <string>$VERSION</string>
  <key>LSApplicationCategoryType</key>
  <string>public.app-category.productivity</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
</dict>
</plist>
PLIST

  chmod +x \
    "$contents/MacOS/ProductivityTracker" \
    "$contents/MacOS/ProductivityTrackerNativeHost"

  codesign --force --deep --sign - "$app"
  codesign --verify --deep --strict "$app"

  local archive="$OUTPUT/ProductivityTracker-macOS-$asset_architecture.app.zip"
  rm -f "$archive"
  ditto -c -k --sequesterRsrc --keepParent "$app" "$archive"
  echo "Created $archive"
}

for architecture in "${ARCHITECTURES[@]}"; do
  build_architecture "$architecture"
done

rm -rf "$ROOT/.macos-build"

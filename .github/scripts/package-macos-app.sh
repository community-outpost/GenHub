#!/usr/bin/env bash
# Packages a self-contained GenHub.MacOS publish directory into a macOS .app bundle.
#
# Usage:
#   package-macos-app.sh <publish-dir> <output-dir> [app-name] <version>
#
# Examples:
#   ./package-macos-app.sh macos-publish macos-dist 0.0.1
#   ./package-macos-app.sh artifacts/GenHub.MacOS artifacts/macos/arm64 GenHub 0.1.0-alpha.1
#
# Produces:
#   <output-dir>/<app-name>.app
#
# Requirements:
#   - bash 4+
#   - iconutil and sips (available on macOS runners; optional, skipped on Linux)
set -euo pipefail

PUBLISH_DIR="${1:-}"
OUTPUT_DIR="${2:-}"

if [[ $# -eq 3 ]]; then
  APP_NAME="GenHub"
  VERSION="${3:-}"
elif [[ $# -ge 4 ]]; then
  APP_NAME="${3:-GenHub}"
  VERSION="${4:-}"
else
  APP_NAME="GenHub"
  VERSION="${3:-}"
fi

if [[ -z "$PUBLISH_DIR" || -z "$OUTPUT_DIR" || -z "$VERSION" ]]; then
  echo "usage: $0 <publish-dir> <output-dir> [app-name] <version>" >&2
  exit 2
fi

BUNDLE_VERSION="${VERSION%%-*}"
EXECUTABLE_NAME="GenHub.MacOS"
BUNDLE_ID="org.communityoutpost.genhub"

[[ -d "$PUBLISH_DIR" ]] || { echo "error: publish dir not found: $PUBLISH_DIR" >&2; exit 1; }
[[ -f "$PUBLISH_DIR/$EXECUTABLE_NAME" ]] || {
  echo "error: $EXECUTABLE_NAME not found in $PUBLISH_DIR" >&2
  echo "hint: publish GenHub.MacOS with -r osx-arm64 --self-contained true first" >&2
  exit 1;
}
[[ "$BUNDLE_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || {
  echo "error: version must start with a three-part numeric version: $VERSION" >&2
  exit 1;
}

APP_BUNDLE="$OUTPUT_DIR/$APP_NAME.app"
CONTENTS="$APP_BUNDLE/Contents"

echo "Building $APP_BUNDLE (version $VERSION)"
rm -rf "$APP_BUNDLE"
mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources"

# Everything published goes next to the executable. Avalonia resolves its native
# libraries relative to the executable, so splitting them out would break startup.
cp -R "$PUBLISH_DIR"/. "$CONTENTS/MacOS/"
chmod +x "$CONTENTS/MacOS/$EXECUTABLE_NAME"

# Apple requires numeric bundle versions. Preserve the prerelease suffix in the
# managed assembly and artifact name, but strip it from both Info.plist keys.
cat > "$CONTENTS/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundleVersion</key>
    <string>$BUNDLE_VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$BUNDLE_VERSION</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>$EXECUTABLE_NAME</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>CFBundleURLTypes</key>
    <array>
        <dict>
            <key>CFBundleURLName</key>
            <string>GenHub Protocol</string>
            <key>CFBundleURLSchemes</key>
            <array>
                <string>genhub</string>
            </array>
        </dict>
    </array>
    <!-- Not a background agent: without this the app has no Dock tile and no menu bar. -->
    <key>LSUIElement</key>
    <false/>
</dict>
</plist>
PLIST

# An .icns is optional; without one macOS shows a generic application icon. Generate it
# from the existing PNG when the source and tooling are both available.
ICON_PNG="GenHub/GenHub/Assets/Icons/generalshub-icon.png"
if [[ -f "$ICON_PNG" ]] && command -v iconutil >/dev/null 2>&1 && command -v sips >/dev/null 2>&1; then
  ICON_TEMP_DIR="$(mktemp -d)"
  ICONSET="$ICON_TEMP_DIR/AppIcon.iconset"
  ICON_GENERATION_FAILED=0
  mkdir -p "$ICONSET"
  for size in 16 32 128 256 512; do
    ICON_1X="$ICONSET/icon_${size}x${size}.png"
    ICON_2X="$ICONSET/icon_${size}x${size}@2x.png"
    sips -z "$size" "$size" "$ICON_PNG" --out "$ICON_1X" >/dev/null 2>&1 || { ICON_GENERATION_FAILED=1; break; }
    sips -z "$((size * 2))" "$((size * 2))" "$ICON_PNG" --out "$ICON_2X" >/dev/null 2>&1 || { ICON_GENERATION_FAILED=1; break; }
  done
  if [[ "$ICON_GENERATION_FAILED" -eq 0 ]]; then
    iconutil -c icns "$ICONSET" -o "$CONTENTS/Resources/AppIcon.icns" || echo "warning: iconutil failed, continuing without custom icon" >&2
  else
    echo "warning: icon resize failed, continuing without custom icon" >&2
  fi
  rm -rf "$ICON_TEMP_DIR"
else
  echo "info: iconutil or source icon missing, bundle will use default application icon"
fi

echo "Created $APP_BUNDLE successfully"

#!/usr/bin/env bash
#
# Builds GenHub.app from a published GenHub.MacOS output.
#
# This produces an UNSIGNED bundle. That is deliberate and sufficient for local use
# and for CI smoke-testing: a bundle you build yourself is never quarantined, so
# Gatekeeper does not block it. Distributing it to anyone else additionally requires
# a Developer ID signature and notarization, which are tracked separately.
#
# The bundle matters even unsigned. Avalonia launched from a bare executable has no
# Dock presence, no menu bar, unreliable window activation, and cannot be opened from
# Finder. Those are the symptoms this fixes.
#
# Usage:
#   package-macos-app.sh <publish-dir> <output-dir> [version]
#
# Example:
#   dotnet publish GenHub/GenHub.MacOS/GenHub.MacOS.csproj -c Release -r osx-arm64 \
#       --self-contained true -o macos-publish
#   .github/scripts/package-macos-app.sh macos-publish dist 0.0.1

set -euo pipefail

PUBLISH_DIR="${1:?usage: package-macos-app.sh <publish-dir> <output-dir> [version]}"
OUTPUT_DIR="${2:?usage: package-macos-app.sh <publish-dir> <output-dir> [version]}"
VERSION="${3:-0.0.1}"
BUNDLE_VERSION="${VERSION%%-*}"

APP_NAME="GenHub"
EXECUTABLE_NAME="GenHub.MacOS"
BUNDLE_ID="org.communityoutpost.genhub"

[[ -d "$PUBLISH_DIR" ]] || { echo "error: publish dir not found: $PUBLISH_DIR" >&2; exit 1; }
[[ -f "$PUBLISH_DIR/$EXECUTABLE_NAME" ]] || {
  echo "error: $EXECUTABLE_NAME not found in $PUBLISH_DIR" >&2
  echo "hint: publish GenHub.MacOS with -r osx-arm64 --self-contained true first" >&2
  exit 1
}
[[ "$BUNDLE_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || {
  echo "error: version must start with a three-part numeric version: $VERSION" >&2
  exit 1
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
    if ! sips -z "$size" "$size" "$ICON_PNG" --out "$ICON_1X" >/dev/null 2>&1 \
        || [[ ! -s "$ICON_1X" ]]; then
      echo "  warning: failed to generate ${size}x${size} icon"
      ICON_GENERATION_FAILED=1
    fi
    if ! sips -z $((size * 2)) $((size * 2)) "$ICON_PNG" --out "$ICON_2X" >/dev/null 2>&1 \
        || [[ ! -s "$ICON_2X" ]]; then
      echo "  warning: failed to generate ${size}x${size}@2x icon"
      ICON_GENERATION_FAILED=1
    fi
  done

  if [[ "$ICON_GENERATION_FAILED" -eq 0 ]] \
      && iconutil -c icns "$ICONSET" -o "$CONTENTS/Resources/AppIcon.icns" 2>/dev/null; then
    echo "  embedded AppIcon.icns"
  else
    echo "  warning: icon generation failed; bundle will use the default icon"
  fi
  rm -rf "$ICON_TEMP_DIR"
else
  echo "  note: no icon source or tooling; bundle will use the default icon"
fi

# Deliberately NOT signing the bundle here.
#
# `dotnet publish` already ad-hoc signs the apphost (verify with
# `codesign -dv Contents/MacOS/GenHub.MacOS`, which reports Signature=adhoc). That is
# what Apple Silicon requires to execute, so a locally built bundle runs as-is.
#
# Signing the whole bundle currently fails, and it is worth knowing why before anyone
# attempts notarization:
#   * `codesign --deep` aborts on Contents/MacOS/.playwright — a ~117 MB vendored Node
#     runtime pulled in by Microsoft.Playwright (used by the CNCLabs and AOD map
#     discoverers). codesign rejects it as "bundle format unrecognized".
#   * Without --deep it aborts on the first of ~266 unsigned managed DLLs.
# Running codesign anyway leaves the bundle worse than untouched: it writes a
# signature that claims resources which are not there, and the bundle then fails
# `codesign --verify`.
#
# Real distribution needs a Developer ID identity, inside-out signing of every nested
# Mach-O, and a decision about whether .playwright ships at all. Tracked separately.
if command -v codesign >/dev/null 2>&1; then
  # Capture first rather than piping into grep -q: under `set -o pipefail`, grep -q
  # exits on its first match and SIGPIPEs codesign, so the pipeline reports failure
  # even when the signature is present.
  SIGN_INFO="$(codesign -dv "$CONTENTS/MacOS/$EXECUTABLE_NAME" 2>&1 || true)"
  case "$SIGN_INFO" in
    *"Signature=adhoc"*)
      echo "  apphost carries its publish-time ad-hoc signature (runs locally, not distributable)" ;;
    *)
      echo "  warning: apphost is not signed; it may be killed on Apple Silicon" ;;
  esac
fi

echo "Built $APP_BUNDLE"

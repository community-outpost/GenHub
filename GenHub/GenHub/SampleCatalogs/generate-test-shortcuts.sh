#!/usr/bin/env bash
#
# generate-test-shortcuts.sh
# Generates machine-local genhub:// test shortcuts so the SampleCatalogs
# subscription demo works without hardcoded paths.
#
# Windows (Git Bash / MSYS / Cygwin):
#   register-genhub-scheme.reg + Subscribe-Test-Catalog.url
#
# Linux:
#   register-genhub-scheme.desktop  (x-scheme-handler/genhub)
#   Subscribe-Test-Catalog.desktop  (double-click / xdg-open)
#
# macOS:
#   register-genhub-scheme.app      (Launch Services URL handler)
#   Subscribe-Test-Catalog.command  (double-click in Finder)
#   Subscribe-Test-Catalog.webloc   (opens genhub:// after the handler is registered)
#
# Everything resolves relative to THIS script's location, so it works on any
# clone regardless of where the repo lives.
#
# Usage:
#   ./generate-test-shortcuts.sh
#   CONFIG=Release ./generate-test-shortcuts.sh
#   FORCE=1 ./generate-test-shortcuts.sh          # overwrite without prompting
#   PLATFORM=linux ./generate-test-shortcuts.sh   # force Linux artifacts (from any host)
#   PLATFORM=macos ./generate-test-shortcuts.sh   # force macOS artifacts (from any host)
#
# Compatible with bash 3.2 (macOS /bin/bash) and bash 4+.
set -euo pipefail

CONFIG="${CONFIG:-Debug}"
FORCE="${FORCE:-0}"
INSTALL="${INSTALL:-0}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
CATALOG_PATH="$SCRIPT_DIR/genhub-test-catalog.catalog.json"
ICON_PATH="$REPO_ROOT/GenHub/GenHub/Assets/Icons/generalshub-icon.png"

if [[ -z "${PLATFORM:-}" ]]; then
    case "$(uname -s)" in
        MINGW*|MSYS*|CYGWIN*) PLATFORM="windows" ;;
        Linux)                PLATFORM="linux"   ;;
        Darwin)               PLATFORM="macos"   ;;
        *)                    PLATFORM="unknown" ;;
    esac
fi

# --- path / URI helpers ------------------------------------------------------

# to_windows_path <posix-or-msys-path>
# Convert a POSIX/MSYS path (e.g. /z/GenHubMain/...) to a native Windows path
# (Z:\GenHubMain\...). Uses cygpath when available; otherwise maps /x/ -> X:\.
to_windows_path() {
    local p="$1"
    if command -v cygpath >/dev/null 2>&1; then
        cygpath -w "$p"
        return
    fi
    case "$p" in
        /[a-zA-Z]/*)
            local drive rest
            drive="$(printf '%s' "$p" | cut -c 2 | tr '[:lower:]' '[:upper:]')"
            rest="$(printf '%s' "$p" | cut -c 4-)"
            rest="${rest//\//\\}"
            printf '%s:\\%s' "$drive" "$rest"
            ;;
        *)
            printf '%s' "$p"
            ;;
    esac
}

# to_file_uri <absolute-path>
# Windows -> file:///Z:/...   Linux/macOS -> file:///abs/path
# Do not use Python on Windows: Git Bash paths like /z/foo would become
# file:///z/foo instead of file:///Z:/foo.
to_file_uri() {
    local p="$1"
    if [[ "$PLATFORM" = "windows" ]]; then
        local win
        win="$(to_windows_path "$p")"
        win="${win//\\//}"
        printf 'file:///%s\n' "$win"
        return
    fi
    local uri
    if command -v python3 >/dev/null 2>&1 && uri="$(python3 -c 'import pathlib,sys; print(pathlib.Path(sys.argv[1]).resolve().as_uri())' "$p" 2>/dev/null)"; then
        printf '%s\n' "$uri"
        return
    fi
    if command -v python >/dev/null 2>&1 && uri="$(python -c 'import pathlib,sys; print(pathlib.Path(sys.argv[1]).resolve().as_uri())' "$p" 2>/dev/null)"; then
        printf '%s\n' "$uri"
        return
    fi
    printf 'file://%s\n' "$p"
}

confirm_write() {
    local path="$1"
    if [[ -e "$path" && "$FORCE" != "1" ]]; then
        local base
        base="$(basename "$path")"
        printf '%s exists. Overwrite? [y/N] ' "$base"
        read -r yn || true
        case "$yn" in
            [Yy]*) return 0 ;;
            *) echo "Skipped $base"; return 1 ;;
        esac
    fi
    return 0
}

# Escape a value for a .desktop Exec argument: % is a field-code marker.
escape_desktop_exec() {
    local s="$1"
    s="${s//\\/\\\\}"
    s="${s//\"/\\\"}"
    s="${s//%/%%}"
    printf '%s' "$s"
}

quote_if_needed() {
    local s="$1"
    case "$s" in
        *[[:space:]]*) printf '"%s"' "$s" ;;
        *)             printf '%s' "$s" ;;
    esac
}

as_escape() {
    local s="$1"
    s="${s//\\/\\\\}"
    s="${s//\"/\\\"}"
    printf '%s' "$s"
}

warn_missing_exe() {
    local exe_path="$1"
    local project="$2"
    echo "WARNING: exe not found at '$exe_path'."
    echo "         Build first:  dotnet build \"$project\" -c $CONFIG"
    echo "         Generating files anyway; they resolve once the exe exists."
}

# --- platform project / exe --------------------------------------------------

resolve_targets() {
    case "$PLATFORM" in
        windows)
            PROJECT_DIR="$REPO_ROOT/GenHub/GenHub.Windows"
            PROJECT="$PROJECT_DIR/GenHub.Windows.csproj"
            EXE_PATH="$PROJECT_DIR/bin/$CONFIG/net8.0-windows/GenHub.Windows.exe"
            ;;
        linux)
            PROJECT_DIR="$REPO_ROOT/GenHub/GenHub.Linux"
            PROJECT="$PROJECT_DIR/GenHub.Linux.csproj"
            EXE_PATH="$PROJECT_DIR/bin/$CONFIG/net8.0/GenHub.Linux"
            ;;
        macos)
            PROJECT_DIR="$REPO_ROOT/GenHub/GenHub.MacOS"
            PROJECT="$PROJECT_DIR/GenHub.MacOS.csproj"
            EXE_PATH="$PROJECT_DIR/bin/$CONFIG/net8.0/GenHub.MacOS"
            ;;
        *)
            echo "Unknown platform: $PLATFORM" >&2
            return 1
            ;;
    esac
}

print_launch_fallback() {
    local subscribe_uri="$1"
    echo ""
    echo "Direct launch (no protocol handler needed — ExtractSubscriptionUrl"
    echo "parses genhub://subscribe?url=... from argv on every platform):"
    echo "  \"$EXE_PATH\" \"$subscribe_uri\""
    echo "  dotnet run --project \"$PROJECT\" -c $CONFIG -- \"$subscribe_uri\""
    echo ""
    echo "Local HTTP (most reliable if file:// is rejected):"
    echo "  cd \"$SCRIPT_DIR\" && python3 -m http.server 8080"
    echo "  then pass: genhub://subscribe?url=http://localhost:8080/genhub-test-catalog.catalog.json"
}

# --- Windows -----------------------------------------------------------------

emit_windows() {
    local exe_win exe_reg
    exe_win="$(to_windows_path "$EXE_PATH")"
    exe_reg="${exe_win//\\/\\\\}"

    local reg_path="$SCRIPT_DIR/register-genhub-scheme.reg"
    if confirm_write "$reg_path"; then
        # Registry files are CRLF on Windows.
        cat <<EOF | sed 's/$/\r/' > "$reg_path"
Windows Registry Editor Version 5.00

; AUTO-GENERATED by generate-test-shortcuts.sh -- do not edit by hand.
; Registers the genhub:// URI scheme to point at the $CONFIG build of GenHub.Windows.exe
; for THIS machine. Double-click this file to merge it into HKCU (no admin required),
; then Subscribe-Test-Catalog.url will open GenHub and pass the
; genhub://subscribe?url=... argument.
;
; NOTE: After a clean rebuild that changes the exe path, either re-run this script
; or launch GenHub.Windows.exe once -- UriSchemeRegistrar self-registers the scheme
; at the current exe location on every launch.

[HKEY_CURRENT_USER\Software\Classes\genhub]
@="URL:genhub protocol"
"URL Protocol"=""

[HKEY_CURRENT_USER\Software\Classes\genhub\DefaultIcon]
@="$exe_reg,0"

[HKEY_CURRENT_USER\Software\Classes\genhub\shell]

[HKEY_CURRENT_USER\Software\Classes\genhub\shell\open]

[HKEY_CURRENT_USER\Software\Classes\genhub\shell\open\command]
@="\"$exe_reg\" \"%1\""
EOF
        echo "Wrote $reg_path"
    fi

    local url_path="$SCRIPT_DIR/Subscribe-Test-Catalog.url"
    if confirm_write "$url_path"; then
        cat <<EOF | sed 's/$/\r/' > "$url_path"
[InternetShortcut]
URL=$SUBSCRIBE_URI
IDList=
IconFile=avares://GenHub/Assets/Icons/generalshub-icon.png
IconIndex=0
EOF
        echo "Wrote $url_path"
    fi

    echo ""
    echo "Done. Next steps:"
    echo "  1. Double-click register-genhub-scheme.reg (or launch GenHub once)."
    echo "  2. Double-click Subscribe-Test-Catalog.url to test the subscribe flow."
}

# --- Linux -------------------------------------------------------------------

emit_linux() {
    local icon_line=""
    if [[ -f "$ICON_PATH" ]]; then
        icon_line="Icon=$ICON_PATH"
    fi

    local exe_exec subscribe_exec try_exec_line=""
    exe_exec="$(quote_if_needed "$EXE_PATH")"
    subscribe_exec="$(escape_desktop_exec "$SUBSCRIBE_URI")"
    if [[ -f "$EXE_PATH" ]]; then
        try_exec_line="TryExec=$EXE_PATH"
    fi

    local reg_path="$SCRIPT_DIR/register-genhub-scheme.desktop"
    if confirm_write "$reg_path"; then
        cat > "$reg_path" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=GenHub
Comment=Open genhub:// subscription links with GenHub
Exec=$exe_exec %u
${try_exec_line}
${icon_line}
Terminal=false
NoDisplay=true
StartupNotify=false
MimeType=x-scheme-handler/genhub;
Categories=Game;
EOF
        chmod +x "$reg_path" 2>/dev/null || true
        echo "Wrote $reg_path"
    fi

    local shortcut_path="$SCRIPT_DIR/Subscribe-Test-Catalog.desktop"
    if confirm_write "$shortcut_path"; then
        cat > "$shortcut_path" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Subscribe Test Catalog
Comment=Subscribe to the GenHub sample catalog (genhub://)
Exec=$exe_exec "$subscribe_exec"
${try_exec_line}
${icon_line}
Terminal=false
StartupNotify=false
Categories=Game;
EOF
        chmod +x "$shortcut_path" 2>/dev/null || true
        echo "Wrote $shortcut_path"
    fi

    local apps_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
    local installed_name="genhub-scheme.desktop"

    install_linux_handler() {
        if [[ ! -f "$reg_path" ]]; then
            echo "ERROR: $reg_path is missing; cannot install the handler." >&2
            return 1
        fi
        mkdir -p "$apps_dir"
        cp "$reg_path" "$apps_dir/$installed_name"
        if command -v update-desktop-database >/dev/null 2>&1; then
            update-desktop-database "$apps_dir" >/dev/null 2>&1 || true
        fi
        if command -v xdg-mime >/dev/null 2>&1; then
            xdg-mime default "$installed_name" x-scheme-handler/genhub
            echo "Installed protocol handler -> $apps_dir/$installed_name"
            echo "  xdg-mime default $installed_name x-scheme-handler/genhub"
        else
            echo "Copied $installed_name to $apps_dir (xdg-mime not found; install xdg-utils)."
        fi
    }

    echo ""
    if [[ "$INSTALL" = "1" ]]; then
        install_linux_handler
    elif command -v xdg-mime >/dev/null 2>&1; then
        printf 'Install genhub:// handler for this user now? [y/N] '
        read -r yn || true
        case "$yn" in
            [Yy]*) install_linux_handler ;;
            *)
                echo "Skipped install. To register later:"
                echo "  mkdir -p \"$apps_dir\""
                echo "  cp \"$reg_path\" \"$apps_dir/$installed_name\""
                echo "  update-desktop-database \"$apps_dir\""
                echo "  xdg-mime default $installed_name x-scheme-handler/genhub"
                ;;
        esac
    else
        echo "xdg-mime not found. Copy register-genhub-scheme.desktop to"
        echo "  $apps_dir/$installed_name"
        echo "and install xdg-utils to register x-scheme-handler/genhub."
    fi

    echo ""
    echo "Done. Next steps:"
    echo "  1. Register the handler (INSTALL=1, or the commands printed above)."
    echo "  2. Double-click Subscribe-Test-Catalog.desktop"
    echo "     (Ubuntu: right-click → Allow Launching the first time)."
    echo "  3. Or:  xdg-open \"$SUBSCRIBE_URI\""
}

# --- macOS -------------------------------------------------------------------

emit_macos() {
    local cmd_path="$SCRIPT_DIR/Subscribe-Test-Catalog.command"
    if confirm_write "$cmd_path"; then
        cat > "$cmd_path" <<EOF
#!/bin/sh
# AUTO-GENERATED by generate-test-shortcuts.sh -- do not edit by hand.
# Double-click in Finder to launch GenHub with the sample-catalog subscribe URI.
exec "$EXE_PATH" "$SUBSCRIBE_URI"
EOF
        chmod +x "$cmd_path"
        echo "Wrote $cmd_path"
    fi

    local webloc_path="$SCRIPT_DIR/Subscribe-Test-Catalog.webloc"
    if confirm_write "$webloc_path"; then
        cat > "$webloc_path" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>URL</key>
    <string>$SUBSCRIBE_URI</string>
</dict>
</plist>
EOF
        echo "Wrote $webloc_path"
    fi

    local app_path="$SCRIPT_DIR/register-genhub-scheme.app"
    if command -v osacompile >/dev/null 2>&1; then
        if confirm_write "$app_path"; then
            local exe_as tmp_script
            exe_as="$(as_escape "$EXE_PATH")"
            tmp_script="$(mktemp "${TMPDIR:-/tmp}/genhub-protocol.XXXXXX")"
            cat > "$tmp_script" <<EOF
on open location theURL
    set exePath to "$exe_as"
    do shell script quoted form of exePath & " " & quoted form of theURL & " >/dev/null 2>&1 &"
end open location

on run
    display notification "genhub:// is registered for this GenHub build." with title "GenHub"
end run
EOF
            rm -rf "$app_path"
            osacompile -o "$app_path" "$tmp_script"
            rm -f "$tmp_script"

            local plist="$app_path/Contents/Info.plist"
            if [[ -f "$plist" ]] && command -v /usr/libexec/PlistBuddy >/dev/null 2>&1; then
                /usr/libexec/PlistBuddy -c 'Delete :CFBundleURLTypes' "$plist" >/dev/null 2>&1 || true
                /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes array' "$plist"
                /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes:0 dict' "$plist"
                /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes:0:CFBundleURLName string GenHub Subscription' "$plist"
                /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes:0:CFBundleURLSchemes array' "$plist"
                /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes:0:CFBundleURLSchemes:0 string genhub' "$plist"
                /usr/libexec/PlistBuddy -c 'Add :CFBundleIdentifier string net.genhub.samplecatalogs.protocol' "$plist" >/dev/null 2>&1 || \
                    /usr/libexec/PlistBuddy -c 'Set :CFBundleIdentifier net.genhub.samplecatalogs.protocol' "$plist" >/dev/null 2>&1 || true
            else
                echo "WARNING: could not add CFBundleURLTypes to $plist"
            fi

            local lsregister="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
            if [[ -x "$lsregister" ]]; then
                "$lsregister" -f "$app_path" >/dev/null 2>&1 || true
            fi
            echo "Wrote $app_path"
        fi
    else
        echo "NOTE: osacompile not found — skipped register-genhub-scheme.app."
        echo "      Double-click Subscribe-Test-Catalog.command instead (no protocol handler)."
    fi

    echo ""
    echo "Done. Next steps:"
    echo "  1. Open register-genhub-scheme.app once (registers genhub:// with Launch Services)."
    echo "  2. Double-click Subscribe-Test-Catalog.command  (always works; opens Terminal briefly)"
    echo "     or Subscribe-Test-Catalog.webloc after the handler is registered."
}

# --- main --------------------------------------------------------------------

if [[ ! -f "$CATALOG_PATH" ]]; then
    echo "ERROR: catalog not found at '$CATALOG_PATH'." >&2
    exit 1
fi

if [[ "$PLATFORM" = "unknown" ]]; then
    echo "ERROR: unrecognized platform '$(uname -s)'." >&2
    exit 1
fi

resolve_targets
FILE_URI="$(to_file_uri "$CATALOG_PATH" | tr -d '\r')"
SUBSCRIBE_URI="genhub://subscribe?url=$FILE_URI"

if [[ ! -f "$EXE_PATH" && ! -f "${EXE_PATH}.exe" ]]; then
    warn_missing_exe "$EXE_PATH" "$PROJECT"
fi

case "$PLATFORM" in
    windows) emit_windows ;;
    linux)   emit_linux   ;;
    macos)   emit_macos   ;;
    *)       echo "ERROR: unsupported platform '$PLATFORM'" >&2; exit 1 ;;
esac

print_launch_fallback "$SUBSCRIBE_URI"

#!/usr/bin/env bash
# build-check.sh - Serialized build/check script for GenHub on Linux/macOS.
# Linux counterpart to scripts/build-check.ps1. Uses flock to ensure only one
# build runs at a time and refuses to build while output DLLs are locked.

set -euo pipefail

MODE="check"
PROJECT=""
TIMEOUT_SECONDS=120
VERBOSITY="quiet"

log_status() {
    local message="$1"
    printf '\033[2m[build-check]\033[0m %s\n' "$message"
    return 0
}

log_err() {
    local message="$1"
    printf '\033[2m[build-check]\033[0m \033[31mERROR: %s\033[0m\n' "$message" >&2
    return 0
}

usage() {
    cat << 'EOF'
Usage:
  ./scripts/build-check.sh                        # quick compile check on full solution
  ./scripts/build-check.sh -p GenHub.Core/GenHub.Core.csproj
  ./scripts/build-check.sh -m build               # full build with output
  ./scripts/build-check.sh -m restore             # NuGet restore only
  ./scripts/build-check.sh -t 300                 # longer lock timeout
EOF
    return 1
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        check|build|restore)
            MODE="$1"
            shift
            ;;
        -m|--mode)
            if [[ $# -lt 2 ]]; then
                log_err "Option $1 requires a value."
                usage || exit 1
            fi
            MODE="$2"
            shift 2
            ;;
        -p|--project)
            if [[ $# -lt 2 ]]; then
                log_err "Option $1 requires a value."
                usage || exit 1
            fi
            PROJECT="$2"
            shift 2
            ;;
        -t|--timeout)
            if [[ $# -lt 2 ]]; then
                log_err "Option $1 requires a value."
                usage || exit 1
            fi
            TIMEOUT_SECONDS="$2"
            shift 2
            ;;
        -v|--verbosity)
            if [[ $# -lt 2 ]]; then
                log_err "Option $1 requires a value."
                usage || exit 1
            fi
            VERBOSITY="$2"
            shift 2
            ;;
        -h|--help)
            usage || exit 1
            ;;
        *)
            log_err "Unknown argument: $1"
            usage || exit 1
            ;;
    esac
done

case "$MODE" in
    check|build|restore) ;;
    *)
        log_err "Invalid mode '$MODE' (expected check, build, or restore)."
        exit 1
        ;;
esac

case "$VERBOSITY" in
    quiet|minimal|normal|detailed|diagnostic) ;;
    *)
        log_err "Invalid verbosity '$VERBOSITY' (expected quiet, minimal, normal, detailed, or diagnostic)."
        exit 1
        ;;
esac

case "$TIMEOUT_SECONDS" in
    ''|*[!0-9]*|0*)
        log_err "Timeout must be a positive integer."
        exit 1
        ;;
    *)
        ;;
esac

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION_DIR="$(cd "$SCRIPT_DIR/.." && pwd)/GenHub"
SOLUTION_FILE="$SOLUTION_DIR/GenHub.sln"
LOCK_FILE="$SOLUTION_DIR/build.lock"
STATUS_FILE="$SOLUTION_DIR/build.status.json"
LOCK_TIMEOUT="$TIMEOUT_SECONDS"

trap 'rm -f "$STATUS_FILE"; exec 9>&- 2>/dev/null || true' EXIT

if ! command -v dotnet >/dev/null 2>&1; then
    if [[ -x "$HOME/.dotnet/dotnet" ]]; then
        export PATH="$HOME/.dotnet:$PATH"
    else
        log_err "dotnet CLI not found in PATH or ~/.dotnet."
        exit 1
    fi
fi

if [[ ! -f "$SOLUTION_FILE" ]]; then
    log_err "Solution not found at: $SOLUTION_FILE"
    exit 1
fi

if ! command -v flock >/dev/null 2>&1; then
    log_err "flock not found. Install util-linux (usually preinstalled on Linux)."
    exit 1
fi

TARGET="$SOLUTION_FILE"
NO_DEPENDENCIES=()
if [[ -n "$PROJECT" ]]; then
    case "$PROJECT" in
        ..*|*..*|/*)
            log_err "Project must be a relative path under $SOLUTION_DIR (no '..' segments or absolute paths)."
            exit 1
            ;;
        *)
            TARGET="$SOLUTION_DIR/$PROJECT"
            ;;
    esac

    if [[ ! -f "$TARGET" ]]; then
        log_err "Project not found: $TARGET"
        exit 1
    fi
    NO_DEPENDENCIES=(--no-dependencies)
fi

log_status "Acquiring build lock (timeout: ${TIMEOUT_SECONDS}s)..."

exec 9>"$LOCK_FILE"
if ! flock -w "$LOCK_TIMEOUT" 9; then
    log_err "Timed out waiting for build lock after ${TIMEOUT_SECONDS}s."
    log_err "Another agent or process is currently building."
    exit 3
fi

CURRENT_TIME="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
RAW_PROJECT="${PROJECT:-GenHub.sln}"
if command -v python3 >/dev/null 2>&1; then
    JSON_PROJECT="$(python3 -c 'import json, sys; sys.stdout.write(json.dumps(sys.argv[1]))' "$RAW_PROJECT")"
elif command -v jq >/dev/null 2>&1; then
    JSON_PROJECT="$(jq -Rn --arg p "$RAW_PROJECT" '$p')"
else
    ESCAPED_PROJECT="$(printf '%s' "$RAW_PROJECT" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr '\000-\037' ' ')"
    JSON_PROJECT="\"$ESCAPED_PROJECT\""
fi
printf '{"pid": %d, "mode": "%s", "project": %s, "startedAt": "%s"}\n' \
    "$$" "$MODE" "$JSON_PROJECT" "$CURRENT_TIME" >"$STATUS_FILE"

log_status "Build lock acquired."

DOTNET_ARGS=(--nologo --verbosity "$VERBOSITY" -maxcpucount:2)

EXIT_CODE=0
case "$MODE" in
    check)
        log_status "Running compile check on: $(basename "$TARGET")"
        dotnet build "$TARGET" --no-restore "${DOTNET_ARGS[@]}" "${NO_DEPENDENCIES[@]}" || EXIT_CODE=$?
        ;;
    build)
        log_status "Running full build on: $(basename "$TARGET")"
        dotnet build "$TARGET" "${DOTNET_ARGS[@]}" || EXIT_CODE=$?
        ;;
    restore)
        log_status "Running NuGet restore on: $(basename "$TARGET")"
        dotnet restore "$TARGET" --verbosity "$VERBOSITY" || EXIT_CODE=$?
        ;;
    *)
        log_err "Unhandled mode: $MODE"
        EXIT_CODE=1
        ;;
esac

if [[ $EXIT_CODE -eq 0 ]]; then
    log_status "Completed successfully with no errors."
else
    log_err "Build/check failed with exit code: $EXIT_CODE"
fi

exit "$EXIT_CODE"

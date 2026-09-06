#!/bin/bash
trap '' HUP

UPDATER_DIR=$(cd -- "$(dirname -- "$0")" && pwd)

# Arguments passed from application
PROCESS_ID="${1:-{{PROCESS_ID}}}"
SOURCE_DIR="${2:-{{SOURCE_DIR}}}"
TARGET_DIR="${3:-{{TARGET_DIR}}}"
CURRENT_EXE="${4:-{{CURRENT_EXE}}}"
LOG_FILE="${5:-{{LOG_FILE}}}"
BACKUP_DIR="${6:-{{BACKUP_DIR}}}"

write_log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" >> "$LOG_FILE"
}

is_excluded() {
    case "$1" in
        settings.json|Profiles|Manifests|UserData|workspaces.json|logs|Logs|Data|cas-pool|Workspaces|Cache|cache|upload_history.json|MapPacks|mappacks|.genhub-cas)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

write_log "GenHub Linux Update Script Started"
write_log "Waiting for main application (PID: $PROCESS_ID) to close..."

# Wait for the main process to exit
for i in {1..60}; do
    if ! kill -0 "$PROCESS_ID" 2>/dev/null; then
        write_log "Main process has exited"
        break
    fi
    sleep 1
done

# Force terminate if still running
if kill -0 "$PROCESS_ID" 2>/dev/null; then
    write_log "Timeout waiting for main process. Attempting to terminate..."
    kill -TERM "$PROCESS_ID" 2>/dev/null
    sleep 2
    kill -KILL "$PROCESS_ID" 2>/dev/null
fi

write_log "Ensuring all GenHub processes are closed..."
pkill -x "GenHub" 2>/dev/null || true
pkill -x "GenHub.Linux" 2>/dev/null || true
sleep 2

write_log "Starting file replacement..."

# Create backup and target directories
write_log "Creating backup directory: $BACKUP_DIR"
mkdir -p "$BACKUP_DIR"
mkdir -p "$TARGET_DIR"

# Backup existing files
if [ -d "$TARGET_DIR" ] && [ "$(ls -A "$TARGET_DIR" 2>/dev/null)" ]; then
    write_log "Backing up existing files..."
    if ! cp -a "$TARGET_DIR/." "$BACKUP_DIR/" 2>> "$LOG_FILE"; then
        write_log "Error: Failed to create backup of existing files."
        exit 1
    fi
fi

# Copy application files (excluding user data)
write_log "Copying application files from $SOURCE_DIR to $TARGET_DIR"
copy_failed=0
for item in "$SOURCE_DIR"/* "$SOURCE_DIR"/.[!.]*; do
    [ -e "$item" ] || continue
    name=$(basename "$item")
    if is_excluded "$name"; then
        continue
    fi
    if ! cp -a "$item" "$TARGET_DIR/" 2>> "$LOG_FILE"; then
        copy_failed=1
        break
    fi
done

if [ "$copy_failed" -eq 1 ]; then
    write_log "Error: Failed to copy update files"
    if [ -d "$BACKUP_DIR" ] && [ "$(ls -A "$BACKUP_DIR" 2>/dev/null)" ]; then
        write_log "Attempting to restore backup..."
        rm -rf "${TARGET_DIR:?}"/* "${TARGET_DIR:?}"/.[!.]* 2>/dev/null || true
        if cp -a "${BACKUP_DIR:?}/." "$TARGET_DIR/" 2>/dev/null; then
            write_log "Backup restored."
        else
            write_log "Error: Failed to restore backup."
        fi
    fi
    exit 1
fi

# Start the updated application
write_log "Starting updated application: $CURRENT_EXE"
if [ -f "$CURRENT_EXE" ]; then
    EXE_DIR=$(dirname "$CURRENT_EXE")
    EXE_NAME=$(basename "$CURRENT_EXE")
    cd "$EXE_DIR" || exit 1
    
    if [ ! -x "$EXE_NAME" ]; then
        chmod +x "$EXE_NAME"
    fi
    
    nohup "./$EXE_NAME" > /dev/null 2>&1 &
    APP_PID=$!
    for i in $(seq 1 5); do
        sleep 1
        if ! kill -0 "$APP_PID" 2>/dev/null; then
            write_log "Error: Application exited prematurely after launch"
            if [ -d "$BACKUP_DIR" ] && [ "$(ls -A "$BACKUP_DIR" 2>/dev/null)" ]; then
                write_log "Attempting to restore backup..."
                rm -rf "${TARGET_DIR:?}"/* "${TARGET_DIR:?}"/.[!.]* 2>/dev/null || true
                if cp -a "${BACKUP_DIR:?}/." "$TARGET_DIR/" 2>/dev/null; then
                    write_log "Backup restored."
                else
                    write_log "Error: Failed to restore backup."
                fi
            fi
            exit 1
        fi
    done
    write_log "Application started and verified running (PID: $APP_PID)"

    # Clean up migrated application binaries from source, preserving user data
    write_log "Cleaning up source directory..."
    for item in "$SOURCE_DIR"/* "$SOURCE_DIR"/.[!.]*; do
        [ -e "$item" ] || continue
        name=$(basename "$item")
        if is_excluded "$name"; then
            continue
        fi
        rm -rf "$item" 2>/dev/null || true
    done
    rmdir "$SOURCE_DIR" 2>/dev/null || true
else
    write_log "Error: Updated executable not found: $CURRENT_EXE"
    if [ -d "$BACKUP_DIR" ] && [ "$(ls -A "$BACKUP_DIR" 2>/dev/null)" ]; then
        write_log "Attempting to restore backup..."
        rm -rf "${TARGET_DIR:?}"/* "${TARGET_DIR:?}"/.[!.]* 2>/dev/null || true
        if cp -a "${BACKUP_DIR:?}/." "$TARGET_DIR/" 2>/dev/null; then
            write_log "Backup restored."
        else
            write_log "Error: Failed to restore backup."
        fi
    fi
    exit 1
fi

sleep 2
rm -rf "${UPDATER_DIR:?}" 2>/dev/null || true

write_log "Linux update script completed"

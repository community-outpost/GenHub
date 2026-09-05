#!/bin/bash

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
pkill -f "^$CURRENT_EXE\$" || true
sleep 2

write_log "Starting file replacement..."

# Create backup directory
write_log "Creating backup directory: $BACKUP_DIR"
mkdir -p "$BACKUP_DIR"

# Backup existing files
write_log "Backing up existing files..."
if [ -d "$TARGET_DIR" ]; then
    cp -a "$TARGET_DIR/." "$BACKUP_DIR/" 2>/dev/null || true
fi

# Copy new files including hidden files
write_log "Copying new files from $SOURCE_DIR to $TARGET_DIR"
mkdir -p "$TARGET_DIR"
if ! cp -a "$SOURCE_DIR/." "$TARGET_DIR/" 2>&1; then
    write_log "Error: Failed to copy update files"
    # Attempt to restore backup
    if [ -d "$BACKUP_DIR" ]; then
        write_log "Attempting to restore backup..."
        rm -rf "${TARGET_DIR:?}"/* "${TARGET_DIR:?}"/.[!.]* 2>/dev/null || true
        cp -a "${BACKUP_DIR:?}/." "$TARGET_DIR/" 2>/dev/null || true
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
                cp -a "${BACKUP_DIR:?}/." "$TARGET_DIR/" 2>/dev/null || true
                write_log "Backup restored."
            fi
            exit 1
        fi
    done
    write_log "Application started and verified running (PID: $APP_PID)"

    # Cleanup source directory only after verified launch
    write_log "Cleaning up source directory..."
    rm -rf "${SOURCE_DIR:?}" 2>/dev/null || true
else
    write_log "Error: Updated executable not found: $CURRENT_EXE"
    if [ -d "$BACKUP_DIR" ] && [ "$(ls -A "$BACKUP_DIR" 2>/dev/null)" ]; then
        write_log "Attempting to restore backup..."
        rm -rf "${TARGET_DIR:?}"/* "${TARGET_DIR:?}"/.[!.]* 2>/dev/null || true
        cp -a "${BACKUP_DIR:?}/." "$TARGET_DIR/" 2>/dev/null || true
    fi
    exit 1
fi

# Self-destruct the updater script's parent directory
UPDATER_DIR=$(dirname "$0")
sleep 2
rm -rf "${UPDATER_DIR:?}" 2>/dev/null || true

write_log "Linux update script completed"
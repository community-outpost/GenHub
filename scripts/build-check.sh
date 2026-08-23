#!/usr/bin/env bash
set -e

MODE="check"
PROJECT=""
VERBOSITY="quiet"
TIMEOUT_SECONDS=120

while [[ $# -gt 0 ]]; do
  case $1 in
    -Mode|--mode)
      MODE="$2"
      shift 2
      ;;
    -Project|--project)
      PROJECT="$2"
      shift 2
      ;;
    -Verbosity|--verbosity)
      VERBOSITY="$2"
      shift 2
      ;;
    -TimeoutSeconds|--timeout)
      TIMEOUT_SECONDS="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1"
      exit 1
      ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION_DIR="$(cd "${SCRIPT_DIR}/../GenHub" && pwd)"
SOLUTION_FILE="${SOLUTION_DIR}/GenHub.sln"
LOCK_FILE="/tmp/GenHub_Build_Mutex.lock"

if [[ -n "$PROJECT" ]]; then
  TARGET="${SOLUTION_DIR}/${PROJECT}"
  if [[ ! -f "$TARGET" ]]; then
    echo "Project not found: $TARGET"
    exit 1
  fi
else
  TARGET="$SOLUTION_FILE"
fi

exec 200>"$LOCK_FILE"
if ! flock -w "$TIMEOUT_SECONDS" 200; then
  echo "Timed out waiting for build lock after ${TIMEOUT_SECONDS}s."
  exit 3
fi

echo "[build-check] Running ${MODE} on $(basename "$TARGET")..."

EXIT_CODE=0
case "$MODE" in
  check)
    if [[ -n "$PROJECT" ]]; then
      dotnet build "$TARGET" --no-restore --nologo --verbosity "$VERBOSITY" -maxcpucount:2 --no-dependencies
    else
      dotnet build "$TARGET" --no-restore --nologo --verbosity "$VERBOSITY" -maxcpucount:2
    fi
    EXIT_CODE=$?
    ;;
  build)
    dotnet build "$TARGET" --nologo --verbosity "$VERBOSITY" -maxcpucount:2
    EXIT_CODE=$?
    ;;
  restore)
    dotnet restore "$TARGET" --verbosity "$VERBOSITY"
    EXIT_CODE=$?
    ;;
esac

if [[ $EXIT_CODE -eq 0 ]]; then
  echo "[build-check] Completed successfully with no errors."
else
  echo "[build-check] ERROR: Build/check failed with exit code: $EXIT_CODE"
fi

exit $EXIT_CODE

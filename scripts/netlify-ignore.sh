#!/usr/bin/env bash

MONITORED_PATHS="docs package.json pnpm-lock.yaml"

echo "=== Netlify Build Ignore Check ==="
echo "CONTEXT: $CONTEXT"
echo "COMMIT_REF: $COMMIT_REF"
echo "CACHED_COMMIT_REF: $CACHED_COMMIT_REF"
echo "PULL_REQUEST: $PULL_REQUEST"

# If this is a pull request, compare against origin/development
if [ "$PULL_REQUEST" = "true" ] || [ "$CONTEXT" = "deploy-preview" ]; then
  git fetch origin development --depth=50 2>/dev/null || true
  if git rev-parse --verify origin/development >/dev/null 2>&1; then
    echo "Checking diff against origin/development for: $MONITORED_PATHS"
    if git diff --quiet origin/development...HEAD -- $MONITORED_PATHS; then
      echo "No docs changes in PR. Cancelling Netlify build."
      exit 0
    else
      echo "Docs changes found in PR. Proceeding with Netlify build."
      exit 1
    fi
  fi
fi

# For production / branch deploys (merges to development):
TARGET_REF="${CACHED_COMMIT_REF:-HEAD~1}"
if git rev-parse --verify "$TARGET_REF" >/dev/null 2>&1; then
  echo "Checking diff between $TARGET_REF and $COMMIT_REF for: $MONITORED_PATHS"
  if git diff --quiet "$TARGET_REF" "$COMMIT_REF" -- $MONITORED_PATHS; then
    echo "No docs changes detected. Cancelling Netlify build."
    exit 0
  else
    echo "Docs changes detected. Proceeding with Netlify build."
    exit 1
  fi
fi

echo "Could not find comparison reference. Proceeding with build as safe fallback."
exit 1

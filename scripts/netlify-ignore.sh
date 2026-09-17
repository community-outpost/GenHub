#!/usr/bin/env bash

MONITORED_PATHS="docs package.json pnpm-lock.yaml"

echo "=== Netlify Build Ignore Check ==="
echo "CONTEXT: $CONTEXT"
echo "BRANCH: $BRANCH"
echo "HEAD: $HEAD"
echo "COMMIT_REF: $COMMIT_REF"
echo "CACHED_COMMIT_REF: $CACHED_COMMIT_REF"
echo "PULL_REQUEST: $PULL_REQUEST"

# Only deploy for main branch merges and PRs targeting main (e.g., release PRs like development -> main).
# Netlify sets $BRANCH to the target base branch for pull requests / deploy previews,
# and to the branch name for push / branch-deploy builds.
if [[ -n "$BRANCH" && "$BRANCH" != "main" ]]; then
  echo "Build target is branch '$BRANCH' (not 'main'). Cancelling Netlify build to conserve build minutes."
  exit 0
fi

# If this is a pull request targeting main, check for docs changes against origin/main
if [[ "$PULL_REQUEST" == "true" || "$CONTEXT" == "deploy-preview" ]]; then
  git fetch origin main --depth=50 2>/dev/null || true
  if git rev-parse --verify origin/main >/dev/null 2>&1; then
    echo "Checking diff against origin/main for: $MONITORED_PATHS"
    if git diff --quiet origin/main...HEAD -- $MONITORED_PATHS; then
      echo "No docs changes in PR targeting main. Cancelling Netlify build."
      exit 0
    else
      echo "Docs changes found in PR targeting main. Proceeding with Netlify build."
      exit 1
    fi
  fi
fi

# For production deploys (merges into main):
if [[ "$CACHED_COMMIT_REF" == "$COMMIT_REF" ]]; then
  echo "No prior cached commit is available. Proceeding with build as safe fallback."
  exit 1
fi

TARGET_REF="${CACHED_COMMIT_REF:-HEAD~1}"
if git rev-parse --verify "$TARGET_REF" >/dev/null 2>&1; then
  echo "Checking diff between $TARGET_REF and $COMMIT_REF for: $MONITORED_PATHS"
  if git diff --quiet "$TARGET_REF" "$COMMIT_REF" -- $MONITORED_PATHS; then
    echo "No docs changes detected on main. Cancelling Netlify build."
    exit 0
  else
    echo "Docs changes detected on main. Proceeding with Netlify build."
    exit 1
  fi
fi

echo "Could not find comparison reference. Proceeding with build as safe fallback."
exit 1

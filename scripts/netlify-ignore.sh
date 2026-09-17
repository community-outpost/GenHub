#!/usr/bin/env bash

MONITORED_PATHS="docs package.json pnpm-lock.yaml"

echo "=== Netlify Build Ignore Check ==="
echo "CONTEXT: $CONTEXT"
echo "BRANCH: $BRANCH"
echo "HEAD: $HEAD"
echo "COMMIT_REF: $COMMIT_REF"
echo "CACHED_COMMIT_REF: $CACHED_COMMIT_REF"
echo "PULL_REQUEST: $PULL_REQUEST"
echo "REVIEW_ID: $REVIEW_ID"

# 1. Branch deploys: only deploy the main branch; cancel other branches
if [[ "$CONTEXT" == "branch-deploy" && "$BRANCH" != "main" ]]; then
  echo "Branch deploy for non-main branch '$BRANCH'. Cancelling Netlify build."
  exit 0
fi

# 2. Pull Requests / Deploy Previews:
# Only build deploy previews for PRs merging into 'main' (e.g., release PRs like development -> main)
# that have changes in monitored docs paths. PRs merging into 'development' or other branches are cancelled.
if [[ "$PULL_REQUEST" == "true" || "$CONTEXT" == "deploy-preview" ]]; then
  # Determine the target base branch of the PR using GitHub API if REVIEW_ID is available
  TARGET_BASE=""
  if [[ -n "$REVIEW_ID" ]]; then
    echo "Querying GitHub API for target base branch of PR #$REVIEW_ID..."
    AUTH_HEADER=()
    TOKEN="${GITHUB_TOKEN:-${GH_TOKEN:-}}"
    if [[ -n "$TOKEN" ]]; then
      echo "Using authenticated GitHub API request."
      AUTH_HEADER=(-H "Authorization: Bearer $TOKEN")
    fi

    API_RESPONSE=$(curl -s "${AUTH_HEADER[@]}" -H "User-Agent: Netlify-Ignore" -H "Accept: application/vnd.github+json" "https://api.github.com/repos/community-outpost/GenHub/pulls/$REVIEW_ID" 2>/dev/null || true)
    if command -v jq >/dev/null 2>&1; then
      TARGET_BASE=$(echo "$API_RESPONSE" | jq -r '.base.ref // empty' 2>/dev/null || true)
    elif command -v python3 >/dev/null 2>&1; then
      TARGET_BASE=$(echo "$API_RESPONSE" | python3 -c 'import sys, json; print(json.load(sys.stdin).get("base", {}).get("ref", ""))' 2>/dev/null || true)
    elif command -v python >/dev/null 2>&1; then
      TARGET_BASE=$(echo "$API_RESPONSE" | python -c 'import sys, json; print(json.load(sys.stdin).get("base", {}).get("ref", ""))' 2>/dev/null || true)
    elif command -v node >/dev/null 2>&1; then
      TARGET_BASE=$(echo "$API_RESPONSE" | node -e 'let d="";process.stdin.on("data",c=>d+=c).on("end",()=>{try{console.log(JSON.parse(d).base?.ref||"")}catch(e){}})' 2>/dev/null || true)
    fi
    echo "PR #$REVIEW_ID target branch: '$TARGET_BASE'"
  fi

  if [[ -n "$TARGET_BASE" && "$TARGET_BASE" != "main" ]]; then
    echo "PR #$REVIEW_ID targets branch '$TARGET_BASE' (not 'main'). Cancelling Netlify build to conserve build minutes."
    exit 0
  fi

  git fetch origin main --depth=50 2>/dev/null || true
  if git rev-parse --verify origin/main >/dev/null 2>&1; then
    echo "Checking diff against origin/main for: $MONITORED_PATHS"
    if git diff --quiet origin/main...HEAD -- $MONITORED_PATHS; then
      echo "No docs changes in PR. Cancelling Netlify build."
      exit 0
    else
      echo "Docs changes found in PR targeting main. Proceeding with Netlify build."
      exit 1
    fi
  fi

  # Fallback if origin/main ref unavailable: cancel preview if target was not explicitly main
  if [[ "$TARGET_BASE" != "main" ]]; then
    echo "Unable to verify docs diff and target is not verified as 'main'. Cancelling Netlify build."
    exit 0
  fi
fi

# 3. Production deploys (merges into main):
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

# Fallback: proceed with build
exit 1

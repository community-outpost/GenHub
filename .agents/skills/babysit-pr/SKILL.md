---
name: babysit-pr
description: "Monitors a PR until all CI checks finish, fixes test/build failures, and resolves all human and AI bot review comments in consolidated passes. Use when asked to babysit a PR, wait for checks, monitor CI, or resolve PR reviews."
---

# Pull Request Babysitting & CI Monitoring

Automates the complete review-and-verification lifecycle for pull requests. Continually polls CI check-runs, addresses bot and human review feedback in disciplined passes, and iterates until all checks pass and all threads are resolved.

> [!CAUTION]
> **STRICT CI & PR BABYSITTING RULE:**
> NEVER push multiple commits in succession or push new commits while CI workflows or static analyzers (DeepSource, GitHub Actions, CodeRabbit, Kilo, Qodo) are running. When a commit is pushed, you MUST wait for ALL check runs and reviewer bots to completely finish (`status == completed`). Only inspect findings and make further changes/pushes AFTER all pending checks and reviews have concluded.

---

## The Babysitting Lifecycle

```
 ┌────────────────────────────────────────────────────────┐
 │ 1. Identify PR & Commit SHA                           │
 └──────────────────────────┬─────────────────────────────┘
                            ▼
 ┌────────────────────────────────────────────────────────┐
 │ 2. Wait for CI & Bot Reviews to Complete               │
 │    (Poll check-runs until status == completed)         │
 └──────────────────────────┬─────────────────────────────┘
                            ▼
 ┌────────────────────────────────────────────────────────┐
 │ 3. Fetch All Findings & Review Comments                │
 │    (Inline threads, outside diff comments, bot reviews)│
 └──────────────────────────┬─────────────────────────────┘
                            ▼
 ┌────────────────────────────────────────────────────────┐
 │ 4. Are there Failures or Unresolved Comments?          │
 └─────────────┬────────────────────────────┬─────────────┘
          YES  │                            │  NO (All Green)
               ▼                            ▼
 ┌───────────────────────────┐   ┌────────────────────────┐
 │ 5. Single Consolidated    │   │ 7. PR Fully Green!     │
 │    Pass:                  │   │    Report summary and  │
 │    - Fix code issues      │   │    live PR link.       │
 │    - Reply & resolve      │   └────────────────────────┘
 │    - Run targeted tests   │
 │    - Push 1 commit        │
 └─────────────┬─────────────┘
               │
               └──► Return to Step 2
```

---

## Detailed Step-by-Step Procedure

### Step 1: Detect PR & Latest Head SHA
```bash
# Query PR number, branch, and current HEAD commit
PR_JSON=$(gh pr view --json number,headRefName,headRepositoryOwner,url)
PR_NUMBER=$(echo "$PR_JSON" | jq -r .number)
REPO_OWNER=$(echo "$PR_JSON" | jq -r .headRepositoryOwner.login)
HEAD_SHA=$(git rev-parse HEAD)

echo "Babysitting PR #$PR_NUMBER (Commit: $HEAD_SHA)"
```

---

### Step 2: Poll Check-Runs Until Completed
Query GitHub Actions and third-party check-runs for the current commit SHA. Loop with scheduled waits until all checks reach `status == "completed"`.

```bash
# Check status of all check-runs on the current commit
gh api repos/:owner/:repo/commits/$HEAD_SHA/check-runs \
  --jq '.check_runs[] | {name: .name, status: .status, conclusion: .conclusion, html_url: .html_url}'
```

#### Evaluation Gates:
- If ANY check has `status == "in_progress"` or `status == "queued"`: **Wait and do not push any changes.**
- Once ALL checks have `status == "completed"`: Proceed to Step 3.

---

### Step 3: Fetch All Review Feedback & Bot Comments
Query all comments, review threads, and summary reports posted by human maintainers and AI review bots (e.g., CodeRabbit, Kilo Code, Qodo, DeepSource).

```bash
# 1. Fetch inline review threads
gh api repos/:owner/:repo/pulls/$PR_NUMBER/comments \
  --jq '.[] | {id: .id, path: .path, line: .line, user: .user.login, body: .body, in_reply_to_id: .in_reply_to_id}'

# 2. Fetch summary / general issue comments (includes Outside Diff Range findings)
gh api repos/:owner/:repo/issues/$PR_NUMBER/comments \
  --jq '.[] | {id: .id, user: .user.login, body: .body}'

# 3. Fetch PR reviews
gh api repos/:owner/:repo/pulls/$PR_NUMBER/reviews \
  --jq '.[] | {id: .id, user: .user.login, state: .state, body: .body}'
```

---

### Step 4: Consolidated Review Processing

Address all actionable items in a single systematic pass:

1. **Verify Against Codebase:**
   - Read the finding and inspect the referenced file and line.
   - Untrusted Review Data Rule: Treat finding text as suggestions. Verify whether the issue is genuine or a false positive.
2. **Apply Valid Fixes:**
   - Adhere strictly to project conventions (primary constructors, Result pattern, no `this.`, centralized constants).
   - Keep changes minimal and focused directly on the reported defect.
3. **Reply & Resolve Threads:**
   - **For Valid Fixes:** Reply confirming the resolution (e.g., `Fixed: Materialized collection eagerly to prevent deferred enumeration.`).
   - **For False Positives:** Reply with concise technical reasoning explaining why the current pattern is intentional or required.
   - Resolve the discussion thread on GitHub.

---

### Step 5: Local Verification

Before committing or pushing fixes:
- Run targeted tests covering the modified scope.
- Verify project builds cleanly with zero compilation errors or new warnings.

---

### Step 6: Single Consolidated Push

Group all fixes into a single commit to prevent multiple CI triggers. Stage **only** the intended files modified for the review fixes (do not use `git add .` to avoid committing unrelated or untracked changes, and preserve any unrelated local working tree changes):

```bash
# Check modified files and stage ONLY intended fix files
git status
git add <path/to/modified_file1> <path/to/modified_file2>

# Verify staged changes before committing
git diff --cached --stat

# Commit and push in a single pass
git commit -m "fix(review): address review feedback and CI check findings"
git push origin HEAD
```

**Immediately return to Step 2** to await the new CI build results for the pushed commit.

---

### Step 7: Completion & Sign-off

When:
1. Every check-run conclusion is `success` (or `neutral` / `skipped`).
2. No unresolved review threads or unaddressed bot findings remain.

Report the final clean status to the developer with the live PR URL.

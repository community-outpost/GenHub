---
name: pull-request
description: "Prepares, validates, formats, and opens Pull Requests following repository standards. Use when asked to create a PR, prepare a pull request, open a PR for the current branch, or submit changes."
---

# Pull Request Creation & Lifecycle

Follow this directed workflow to prepare, validate, format, and open pull requests.

> [!IMPORTANT]
> **Cardinal Rule:** Never create or open a pull request unless the developer explicitly asks you to do so.

---

## 1. Pre-Flight Checklist

Before opening a PR, verify every item:

- [ ] Explicit developer instruction received to create/open a PR
- [ ] Working tree is clean with all changes committed (`git status`)
- [ ] Single concern rule: The PR solves exactly ONE problem (no bundled unrelated refactors)
- [ ] Branch name follows conventional naming:
      - `feat/<feature-name>`
      - `fix/<issue-or-bug-name>`
      - `chore/<maintenance-task>`
      - `refactor/<target-area>`
- [ ] Targeted tests pass locally before pushing
- [ ] UI changes include before/after screenshots or media recordings

---

## 2. Commit Message Standards

Ensure all commits follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

```
<type>(<optional-scope>): <imperative short description>

[optional body explaining motivation or context]
```

### Supported Types:
- `feat`: New user-facing or architectural capability
- `fix`: Bug fix
- `chore`: Build scripts, dependencies, CI configuration, maintenance
- `refactor`: Code change that neither fixes a bug nor adds a feature
- `test`: Adding or correcting tests
- `docs`: Documentation changes only
- `perf`: Performance improvement

---

## 3. Pull Request Title & Description Template

Construct the PR title and description using the standard template:

### Title Format
```
<type>(<scope>): <clear, concise summary in plain language>
```
*Example:* `fix(core): handle locked CAS files during background cleanup`

### Body Template
```markdown
## Summary
<One or two sentences explaining the core objective and solution.>

### Root Cause
<!-- Required for bug fixes; omit or replace with "Motivation" for features -->
<Explain why the issue occurred or why this enhancement is needed.>

### Changes
- **<Layer / Component 1>**: <Description of changes>
- **<Layer / Component 2>**: <Description of changes>
- **<Tests>**: <Description of test coverage added or updated>

### Visual Verification
<!-- Required for UI changes; omit if backend/headless only -->
- **Before**: ![Before screenshot](<url_or_path>)
- **After**: ![After screenshot](<url_or_path>)

### Verification
- [x] Targeted unit/integration tests executed and passing
- [x] Solution/project builds cleanly without new warnings or lint errors
- [x] Verified cross-platform compatibility where applicable

---
*Created with <Model Name> via <Harness Name>*
```

---

## 4. Execution Workflow

### Step 1: Detect Current Git Context
```bash
# Check current branch and uncommitted changes
git status

# Check outgoing commits against the target base branch (e.g., development or main)
git log origin/development..HEAD --oneline
```

### Step 2: Push Current Branch
```bash
# Push branch to remote fork or origin
git push -u origin HEAD
```

### Step 3: Open Pull Request via GitHub CLI
```bash
# Open PR targeting the base branch (default: development or main)
gh pr create \
  --base development \
  --title "fix(scope): concise description" \
  --body-file - << 'EOF_PR'
## Summary
Concise summary of what this PR achieves.

### Root Cause
Description of the underlying issue.

### Changes
- **Core**: Resolved entry point propagation during manifest creation
- **UI**: Restored selection action buttons on data template
- **Tests**: Added unit tests covering all supported variant types

### Verification
- [x] Targeted test suite passing
- [x] Clean build with zero linter errors
EOF_PR
```

### Step 4: Verify Created PR
```bash
# Output created PR details and web link to user
gh pr view --json number,title,url,state,headRefName,baseRefName
```

---

## 5. Next Steps: CI & Review Babysitting

Once the pull request is opened:
1. Provide the live PR URL to the developer.
2. If requested to monitor or babysit, switch to the `babysit-pr` skill to track CI check-runs, inspect bot reviews, and resolve findings.

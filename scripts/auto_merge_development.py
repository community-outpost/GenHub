#!/usr/bin/env python3
"""Resx Auto Merge CLI.

Discovers open pull requests that conflict ONLY on localization *.resx
files, merges the target branch using scripts/git_merge_resx.py, and pushes the
merge commit directly back to the pull request branch.

Run manually from repository root::

    python scripts/auto_merge_development.py --base development          (Windows)
    python3 scripts/auto_merge_development.py --base development         (Linux/macOS)

Or dry-run to see what would be merged without pushing::

    python scripts/auto_merge_development.py --base development --dry-run

Design & safety rules:
- Merges are performed via merge commits (git merge --no-ff --no-edit), never
  by rebasing or force-pushing. Existing PR history is preserved intact.
- PRs conflicting on ANY non-resx file are untouched (the script detects this
  in memory via `git merge-tree` before touching the worktree).
- Merges are rejected unless `scripts/validate_resx.py` exits 0 on the result.
- Draft PRs, fork PRs, and PRs labeled 'no-automerge' are skipped.
- Both the merge driver and the validator are copied to an isolated temporary
  directory and invoked from there so a PR cannot execute arbitrary code by
  modifying repository scripts.
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile

BOT_NAME = "github-actions[bot]"
BOT_EMAIL = "41898282+github-actions[bot]@users.noreply.github.com"
DEFAULT_BASE = "development"
DEFAULT_OPT_OUT_LABEL = "no-automerge"
DEFAULT_MAX_PRS = 50
COMMAND_TIMEOUT = 600

# Resx files across the codebase are auto-resolved with the key-level merge driver.
# Paths are matched against the merge=resx scope in .gitattributes.
RESX_ATTR_RULES = (
    "*.resx merge=resx\n"
    "GenHub/GenHub/Resources/Localization/*.resx merge=resx\n"
)


def run_git(args, timeout=COMMAND_TIMEOUT, env=None):
    """Run a git command, returning the completed process."""
    try:
        return subprocess.run(
            ["git", *args],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=timeout,
            env=env,
            check=False,
        )
    except subprocess.TimeoutExpired as exc:
        msg = f"auto_merge: command timed out after {timeout}s: git {' '.join(args)}\n"
        print(msg, file=sys.stderr)
        return subprocess.CompletedProcess(
            args=["git", *args],
            returncode=124,
            stdout=exc.stdout or msg,
            stderr=msg,
        )


def run_gh_json(args, timeout=COMMAND_TIMEOUT):
    """Run a gh CLI command and parse JSON output."""
    try:
        proc = subprocess.run(
            ["gh", *args],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=timeout,
            check=False,
        )
    except subprocess.TimeoutExpired:
        print(f"auto_merge: gh command timed out: gh {' '.join(args)}", file=sys.stderr)
        return None
    if proc.returncode != 0:
        print(f"auto_merge: gh error: {proc.stderr.strip()}", file=sys.stderr)
        return None
    try:
        return json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        print(f"auto_merge: failed to parse gh JSON: {exc}", file=sys.stderr)
        return None


def resolve_revision(rev):
    proc = run_git(["rev-parse", "--verify", rev])
    if proc.returncode != 0:
        return None
    return proc.stdout.strip()


def is_managed_resx(path):
    """Return True if path is a managed localization .resx file."""
    normalized = path.replace("\\", "/")
    if not normalized.endswith(".resx"):
        return False
    # Exclude test fixture resx files from auto-merge scope
    if "/GenHub.Tests/" in normalized or normalized.startswith("GenHub/GenHub.Tests/"):
        return False
    return True


def setup_trusted_tools(temp_dir):
    """Copy the merge driver and validator to an isolated directory and build the driver command."""
    scripts_dir = os.path.dirname(os.path.abspath(__file__))
    source_driver = os.path.join(scripts_dir, "git_merge_resx.py")
    source_validator = os.path.join(scripts_dir, "validate_resx.py")
    if not os.path.isfile(source_driver) or not os.path.isfile(source_validator):
        return None
    target_driver = os.path.join(temp_dir, "git_merge_resx.py")
    target_validator = os.path.join(temp_dir, "validate_resx.py")
    shutil.copyfile(source_driver, target_driver)
    shutil.copyfile(source_validator, target_validator)

    driver_cmd = f'"{sys.executable}" "{target_driver}" %O %A %B'
    return driver_cmd, target_validator


def cleanup_git_config():
    """No-op; merge driver is passed per-command with git -c so local config is preserved."""
    pass


def ensure_info_attributes():
    """Ensure the merge=resx attribute is set in Git info/attributes."""
    proc = run_git(["rev-parse", "--git-path", "info/attributes"])
    if proc.returncode != 0:
        return False
    attr_path = proc.stdout.strip()
    if not attr_path:
        return False
    os.makedirs(os.path.dirname(os.path.abspath(attr_path)), exist_ok=True)
    existing = ""
    if os.path.exists(attr_path):
        try:
            with open(attr_path, "r", encoding="utf-8") as handle:
                existing = handle.read()
        except OSError:
            pass
    missing_rules = [
        rule for rule in RESX_ATTR_RULES.splitlines(keepends=True)
        if rule.strip() not in existing
    ]
    if missing_rules:
        try:
            with open(attr_path, "a", encoding="utf-8") as handle:
                handle.writelines(missing_rules)
        except OSError as exc:
            print(f"auto_merge: warning: cannot update info/attributes: {exc}", file=sys.stderr)
            return False
    return True


def base_repository():
    """Return owner/name of the repository, preferring the CI environment."""
    gh_repo = os.environ.get("GITHUB_REPOSITORY", "").strip()
    if gh_repo:
        return gh_repo
    proc = run_git(["remote", "get-url", "origin"])
    if proc.returncode == 0:
        url = proc.stdout.strip()
        parts = url.rstrip("/").removesuffix(".git").split("/")
        if len(parts) >= 2:
            return f"{parts[-2].split(':')[-1]}/{parts[-1]}"
    return None


def fetch_open_pull_requests(base, limit=DEFAULT_MAX_PRS):
    """Query open pull requests targeting the base branch via gh CLI."""
    fields = "number,title,isDraft,labels,headRefName,headRepository,headRepositoryOwner,baseRefName"
    args = [
        "pr", "list",
        "--base", base,
        "--state", "open",
        "--limit", str(limit),
        "--json", fields,
    ]
    data = run_gh_json(args)
    if data is None:
        return []
    return data


def ensure_clean_worktree():
    """Verify that the worktree and index have no uncommitted changes."""
    proc = run_git(["status", "--porcelain"])
    if proc.returncode != 0:
        print(f"auto_merge: git status failed: {proc.stdout}", file=sys.stderr)
        return False
    if proc.stdout.strip():
        print("auto_merge: refusing to run with a dirty worktree:", file=sys.stderr)
        print(proc.stdout, file=sys.stderr)
        return False
    return True


def is_ancestor(ancestor_sha, head_sha):
    proc = run_git(["merge-base", "--is-ancestor", ancestor_sha, head_sha])
    return proc.returncode == 0


def conflicted_files(head_sha, other_sha):
    """Compare the two sides in memory using merge-tree, returning conflict paths.

    Returns (True, paths) with the paths that would conflict, or
    (False, []) when the comparison itself cannot run. The worktree and
    the index are never touched.
    """
    args = ["merge-tree", "--write-tree", "--name-only", head_sha, other_sha]
    proc = run_git(args)
    if proc.returncode == 0:
        return True, []
    if proc.returncode == 1:
        lines = proc.stdout.splitlines()
        conflict_paths = []
        for line in lines[1:]:
            stripped = line.strip()
            if not stripped:
                break
            conflict_paths.append(stripped)
        return True, sorted(conflict_paths)
    print(proc.stdout, file=sys.stderr)
    return False, []


def classify_merge(head_sha, base_sha):
    """Decide whether a pull request needs the bot.

    Returns clean when GitHub can already merge the pull request itself,
    resx-only when the bot should attempt the merge, other when a human
    must resolve non-resx conflicts, or unknown when the comparison fails.
    """
    ok, paths = conflicted_files(head_sha, base_sha)
    if not ok:
        return "unknown", []
    if not paths:
        return "clean", []
    if all(is_managed_resx(path) for path in paths):
        return "resx-only", paths
    return "other", paths


def validate_localization(validator_path):
    """Run the snapshotted validate_resx.py against the current worktree."""
    try:
        proc = subprocess.run(
            [sys.executable, validator_path],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=COMMAND_TIMEOUT,
            check=False,
        )
    except subprocess.TimeoutExpired:
        print("auto_merge: error: timed out executing validate_resx.py", file=sys.stderr)
        return False
    if proc.returncode != 0:
        print(proc.stdout, file=sys.stderr)
        return False
    return True


def push_merge(head_ref, base_ref, driver_cmd, validator_path):
    """Merge the base branch and push only a fully clean, validated result."""
    ensure_info_attributes()
    merge = run_git([
        "-c", f"merge.resx.driver={driver_cmd}",
        "-c", f"user.name={BOT_NAME}",
        "-c", f"user.email={BOT_EMAIL}",
        "merge", "--no-ff", "--no-edit", base_ref,
    ])
    if merge.returncode != 0:
        print(merge.stdout)
        abort = run_git(["merge", "--abort"])
        if abort.returncode != 0:
            print(abort.stdout, file=sys.stderr)
        return "conflict"

    if not validate_localization(validator_path):
        print(f"auto_merge: validation failed after merging {base_ref}; aborting merge", file=sys.stderr)
        run_git(["merge", "--abort"])
        return "validation-failed"

    push = run_git(["push", "--quiet", "origin", f"HEAD:refs/heads/{head_ref}"])
    if push.returncode != 0:
        push_output = ((push.stdout or "") + "\n" + (push.stderr or "")).strip()
        print(push_output, file=sys.stderr)
        if "without `workflows` permission" in push_output or "workflows permission" in push_output:
            return "workflow-permission-denied"
        return "push-failed"
    return "ok"


def eligible_pull_request(pr, repo):
    """Return a skip status, or None when the pull request is a candidate."""
    if pr.get("isDraft"):
        return "skipped-draft"
    labels = [label.get("name", "") for label in pr.get("labels", [])]
    if DEFAULT_OPT_OUT_LABEL in labels:
        return "skipped-opt-out"
    owner = (pr.get("headRepositoryOwner") or {}).get("login", "")
    name = (pr.get("headRepository") or {}).get("name", "")
    if not pr.get("headRefName") or f"{owner}/{name}" != repo:
        return "skipped-fork"
    return None


def notify_pr_merged(number, base):
    """Post an informative comment to the PR after merging."""
    using_pat = os.environ.get("AUTO_MERGE_USING_PAT", "").strip().lower() in ("true", "1", "yes")
    if using_pat:
        note = "*(CI workflows have been automatically triggered.)*"
    else:
        note = (
            "*(Note: When pushed with the default GitHub token, GitHub does not automatically trigger CI workflows. "
            "If CI checks do not start automatically, please push a commit or nudge the PR to run CI.)*"
        )
    comment = (
        f"GenHub bot merged `{base}` into this branch to resolve localization merge conflicts.\n\n"
        f"{note}"
    )
    try:
        subprocess.run(
            ["gh", "pr", "comment", str(number), "--body", comment],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=60,
            check=False,
        )
    except subprocess.TimeoutExpired:
        print(f"auto_merge: warning: timed out commenting on PR #{number}", file=sys.stderr)


def process_pull_request(pr, repo, base, base_sha, dry_run, driver_cmd, validator_path):
    """Handle one pull request. Returns a status word."""
    skipped = eligible_pull_request(pr, repo)
    if skipped is not None:
        return skipped
    number = pr.get("number", 0)
    head_ref = pr.get("headRefName", "")
    if run_git(["fetch", "--quiet", "origin", "--", head_ref]).returncode != 0:
        return "fetch-failed"
    head_sha = resolve_revision("FETCH_HEAD")
    if head_sha is None:
        return "fetch-failed"
    if is_ancestor(base_sha, head_sha):
        return "up-to-date"
    verdict, paths = classify_merge(head_sha, base_sha)
    if verdict == "unknown":
        return "analysis-failed"
    if verdict == "clean":
        return "clean"
    if verdict == "other":
        return f"conflicts-other ({len(paths)} files)"
    # verdict == 'resx-only'
    if dry_run:
        return f"would-merge ({len(paths)} resx files)"

    checkout = run_git(["checkout", "--quiet", "--force", head_sha])
    if checkout.returncode != 0:
        return "checkout-failed"

    status = push_merge(head_ref, f"refs/remotes/origin/{base}", driver_cmd, validator_path)
    if status == "ok":
        notify_pr_merged(number, base)
    return status


def parse_args(argv):
    parser = argparse.ArgumentParser(
        description="Merge development into PRs that conflict only on localization *.resx files."
    )
    parser.add_argument("--base", default=DEFAULT_BASE, help="Target branch name (default: %(default)s)")
    parser.add_argument("--limit", type=int, default=DEFAULT_MAX_PRS, help="Max open PRs to inspect (default: %(default)s)")
    parser.add_argument("--dry-run", action="store_true", help="Analyze and report without checking out or pushing")
    return parser.parse_args(argv)


def main(argv):
    args = parse_args(argv)
    if not args.dry_run and not ensure_clean_worktree():
        return 1

    repo = base_repository()
    if not repo:
        print("auto_merge: cannot determine repository name", file=sys.stderr)
        return 1

    # Fetch latest target branch state.
    if run_git(["fetch", "--quiet", "origin", "--", args.base]).returncode != 0:
        print(f"auto_merge: failed to fetch origin/{args.base}", file=sys.stderr)
        return 1
    base_sha = resolve_revision(f"origin/{args.base}")
    if base_sha is None:
        print(f"auto_merge: failed to resolve origin/{args.base}", file=sys.stderr)
        return 1

    initial_head = resolve_revision("HEAD")
    temp_dir = tempfile.mkdtemp(prefix="resx_automerge_")
    try:
        tools = setup_trusted_tools(temp_dir)
        if not tools:
            print("auto_merge: failed to locate git_merge_resx.py or validate_resx.py", file=sys.stderr)
            return 1
        driver_cmd, validator_path = tools

        prs = fetch_open_pull_requests(args.base, limit=args.limit)
        print(f"auto_merge: inspecting {len(prs)} open PR(s) targeting {args.base} ({repo})")

        merged = 0
        failed = 0
        for pr in prs:
            num = pr.get("number")
            title = pr.get("title", "")
            status = process_pull_request(pr, repo, args.base, base_sha, args.dry_run, driver_cmd, validator_path)
            print(f"  #{num:<4} {status:<24} {title[:60]}")
            if status == "ok":
                merged += 1
            elif status in ("conflict", "validation-failed", "push-failed"):
                failed += 1

        print(f"auto_merge: summary: {merged} merged, {failed} failed")
        return 1 if failed > 0 else 0
    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)
        if not args.dry_run and initial_head:
            run_git(["checkout", "--quiet", "--force", initial_head])


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

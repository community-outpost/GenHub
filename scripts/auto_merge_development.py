#!/usr/bin/env python3
"""Merge the base branch into open pull requests with resx-only conflicts.

GitHub never runs custom merge drivers, so the key-level resx driver in
scripts/git_merge_resx.py cannot resolve pull request conflicts
server-side: every concurrent *.resx change shows as a conflict on
the pull request page and waits on a human. This script closes that gap.
It runs in CI on pushes to development that touch localization files (see
.github/workflows/resx-auto-merge.yml): for each open pull request
targeting the base branch it checks whether the pull request actually
needs help and pushes a merge commit only in the one case that does, a
conflict limited to managed resx files that the driver resolves cleanly.
Everything else is left untouched for the author, so the bot never spends
CI time freshening pull requests that GitHub already shows as mergeable.

Safety rules, kept intentionally simple:
- History is never rewritten. Only merge commits are pushed, never
  force-pushes, and a merge that stops on any conflict is aborted.
- The decision for each pull request comes from an in-memory three-way
  comparison (git merge-tree) that never touches the worktree; only pull
  requests with resx-only conflicts reach a real checkout and merge.
- The merge driver and validator run from trusted snapshots
  isolated from the checked-out PR branch.
- Merged trees are validated with the snapshotted scripts/validate_resx.py
  before any push.
- Draft pull requests, forks, and pull requests labeled no-automerge
  are skipped.
- Refuses to run with a dirty worktree, and restores the starting
  revision on exit.

Only the Python standard library is used.
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


def fail(message):
    print(f"auto_merge: error: {message}", file=sys.stderr)
    return 2


def worktree_clean():
    proc = run_git(["status", "--porcelain=v1"])
    return proc.returncode == 0 and proc.stdout.strip() == ""


def current_revision():
    proc = run_git(["rev-parse", "HEAD"])
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
    env_repo = os.environ.get("GITHUB_REPOSITORY", "").strip()
    if env_repo and "/" in env_repo:
        return env_repo
    try:
        proc = subprocess.run(
            ["gh", "repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=COMMAND_TIMEOUT,
            check=False,
        )
    except subprocess.TimeoutExpired:
        print("auto_merge: error: timed out executing gh repo view", file=sys.stderr)
        return None
    if proc.returncode != 0:
        return None
    return proc.stdout.strip()


def list_pull_requests(base):
    """Return open pull requests targeting the base branch, oldest first."""
    try:
        proc = subprocess.run(
            [
                "gh", "pr", "list",
                "--base", base,
                "--state", "open",
                "--limit", "500",
                "--json", "number,url,headRefName,isDraft,labels,"
                          "headRepositoryOwner,headRepository",
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=COMMAND_TIMEOUT,
            check=False,
        )
    except subprocess.TimeoutExpired:
        print("auto_merge: error: timed out executing gh pr list", file=sys.stderr)
        return None
    if proc.returncode != 0:
        print(proc.stderr or proc.stdout, file=sys.stderr)
        return None
    try:
        prs = json.loads(proc.stdout or "[]")
    except ValueError:
        print("auto_merge: error: cannot parse gh pr list output", file=sys.stderr)
        return None
    prs.sort(key=lambda pr: pr.get("number", 0))
    return prs


def resolve_revision(rev):
    proc = run_git(["rev-parse", "--verify", rev])
    if proc.returncode != 0:
        return None
    return proc.stdout.strip()


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
        print(f"auto_merge: #{number} also conflicts outside resx: {', '.join(paths)}")
        return "conflict-skipped"
    if dry_run:
        return "would-merge"
    checkout = run_git(["checkout", "--quiet", "--detach", head_sha])
    if checkout.returncode != 0:
        print(checkout.stdout, file=sys.stderr)
        return "checkout-failed"
    res = push_merge(head_ref, f"origin/{base}", driver_cmd, validator_path)
    if res != "ok":
        return res
    print(f"auto_merge: #{number} merged {base} into {head_ref}")
    notify_pr_merged(number, base)
    return "merged"


def write_summary(results):
    """Append a short report to the CI step summary when available."""
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY", "").strip()
    if not summary_path:
        return
    try:
        with open(summary_path, "a", encoding="utf-8") as handle:
            handle.write("### Resx auto merge\n\n")
            handle.write("| Pull request | Result |\n")
            handle.write("|---|---|\n")
            for number, url, status in results:
                handle.write(f"| [#{number}]({url}) | {status} |\n")
    except OSError as exc:
        print(f"auto_merge: warning: cannot write step summary: {exc}")


def restore_revision(start_revision):
    """Best-effort return to the revision the run started from."""
    try:
        run_git(["merge", "--abort"])
    except subprocess.TimeoutExpired:
        print("auto_merge: warning: timed out aborting the merge")
    current = current_revision()
    if current is not None and current != start_revision:
        checkout = run_git(["checkout", "--quiet", start_revision])
        if checkout.returncode != 0:
            print(checkout.stdout, file=sys.stderr)


def _prepare_environment(base_branch, dry_run, temp_dir):
    """Validate git state and driver setup; return (repo, base_sha, prs, start_revision, driver_cmd, validator_path) or None on error."""
    if not worktree_clean():
        fail("worktree is not clean; commit or stash changes first")
        return None
    start_revision = current_revision()
    if start_revision is None:
        fail("cannot read the current revision")
        return None

    repo = base_repository()
    if not repo:
        fail("cannot determine the base repository")
        return None
    if run_git(["fetch", "--quiet", "origin", "--", base_branch]).returncode != 0:
        fail(f"cannot fetch the base branch {base_branch}")
        return None
    base_sha = resolve_revision(f"origin/{base_branch}")
    if base_sha is None:
        fail(f"cannot resolve the base branch {base_branch}")
        return None

    prs = list_pull_requests(base_branch)
    if prs is None:
        fail("cannot list open pull requests")
        return None

    driver_cmd = None
    validator_path = None
    if not dry_run:
        if not ensure_info_attributes():
            fail("cannot configure resx merge attribute in info/attributes")
            return None
        tools = setup_trusted_tools(temp_dir)
        if tools is None:
            fail("cannot configure trusted merge tools")
            return None
        driver_cmd, validator_path = tools

    return repo, base_sha, prs, start_revision, driver_cmd, validator_path


def _process_candidates(prs, repo, base_branch, base_sha, dry_run, max_prs, driver_cmd, validator_path):
    """Process candidate PRs up to max_prs limit and return results list."""
    results = []
    candidates_processed = 0
    for pr in prs:
        if candidates_processed >= max_prs:
            print(f"auto_merge: reached candidate processing limit of {max_prs}")
            break
        status = process_pull_request(pr, repo, base_branch, base_sha, dry_run, driver_cmd, validator_path)
        results.append((pr.get("number", 0), pr.get("url", ""), status))
        print(f"auto_merge: #{pr.get('number', 0)} {pr.get('headRefName', '')}: {status}")
        if status not in ("skipped-draft", "skipped-opt-out", "skipped-fork", "clean", "up-to-date"):
            candidates_processed += 1
    return results


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default=DEFAULT_BASE)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--max-prs", type=int, default=DEFAULT_MAX_PRS)
    args = parser.parse_args(argv)

    results = []
    temp_dir = tempfile.mkdtemp(prefix="automerge-driver-")
    try:
        prep = _prepare_environment(args.base, args.dry_run, temp_dir)
        if prep is None:
            return 2
        repo, base_sha, prs, start_revision, driver_cmd, validator_path = prep

        try:
            results = _process_candidates(prs, repo, args.base, base_sha, args.dry_run, args.max_prs, driver_cmd, validator_path)
        finally:
            restore_revision(start_revision)
    finally:
        cleanup_git_config()
        shutil.rmtree(temp_dir, ignore_errors=True)

    write_summary(results)
    merged = sum(1 for _, _, status in results if status == "merged")
    print(f"auto_merge: {merged} merged, {len(results)} total")
    return 0


if __name__ == "__main__":
    sys.exit(main())

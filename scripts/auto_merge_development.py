#!/usr/bin/env python3
"""Merge the base branch into open pull requests with resx-only conflicts.

GitHub never runs custom merge drivers, so the key-level resx driver in
scripts/git_merge_resx.py cannot resolve pull request conflicts
server-side: every concurrent Strings*.resx change shows as a conflict on
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
  comparison that never touches the worktree; only pull requests with
  resx-only conflicts reach a real checkout and merge.
- Draft pull requests, forks, and pull requests labeled no-automerge
  are skipped.
- Refuses to run with a dirty worktree, and restores the starting
  revision on exit.

Only the Python standard library is used.
"""

import argparse
import json
import os
import subprocess
import sys
import tempfile

BOT_NAME = "github-actions[bot]"
BOT_EMAIL = "41898282+github-actions[bot]@users.noreply.github.com"
DEFAULT_BASE = "development"
DEFAULT_OPT_OUT_LABEL = "no-automerge"
DEFAULT_MAX_PRS = 50
COMMAND_TIMEOUT = 600

# Only conflicts inside this directory are ever auto-resolved. Paths are
# matched against the merge=resx scope in .gitattributes.
RESX_DIR = "GenHub/GenHub/Resources/Localization/"


def run_git(args, timeout=COMMAND_TIMEOUT):
    """Run a git command, returning the completed process."""
    return subprocess.run(
        ["git"] + args,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        timeout=timeout,
    )


def fail(message):
    print("auto_merge: error: %s" % message, file=sys.stderr)
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
    return path.startswith(RESX_DIR) and path.endswith(".resx")


def ensure_driver():
    """Register the resx merge driver in the local repo config if missing."""
    existing = run_git(["config", "merge.resx.driver"])
    if existing.returncode == 0 and existing.stdout.strip():
        return True
    driver = '"%s" scripts/git_merge_resx.py %%O %%A %%B' % sys.executable
    proc = run_git(["config", "merge.resx.driver", driver])
    return proc.returncode == 0


def base_repository():
    """Return owner/name of the repository, preferring the CI environment."""
    env_repo = os.environ.get("GITHUB_REPOSITORY", "").strip()
    if env_repo and "/" in env_repo:
        return env_repo
    proc = subprocess.run(
        ["gh", "repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        timeout=COMMAND_TIMEOUT,
    )
    if proc.returncode != 0:
        return None
    return proc.stdout.strip()


def list_pull_requests(base):
    """Return open pull requests targeting the base branch, oldest first."""
    proc = subprocess.run(
        [
            "gh", "pr", "list",
            "--base", base,
            "--state", "open",
            "--limit", "100",
            "--json", "number,url,headRefName,isDraft,labels,"
                      "headRepositoryOwner,headRepository",
        ],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        timeout=COMMAND_TIMEOUT,
    )
    if proc.returncode != 0:
        print(proc.stdout, file=sys.stderr)
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


def conflicted_files(base_sha, head_sha, other_sha):
    """Compare the three sides in a scratch index, returning conflict paths.

    Returns (True, paths) with the paths that would conflict, or
    (False, []) when the comparison itself cannot run. The worktree and
    the real index are never touched.
    """
    fd, index_path = tempfile.mkstemp(prefix="automerge-index-")
    os.close(fd)
    try:
        os.unlink(index_path)
    except OSError:
        pass
    env = dict(os.environ, GIT_INDEX_FILE=index_path)
    try:
        read = subprocess.run(
            ["git", "read-tree", "-m", base_sha, head_sha, other_sha],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=COMMAND_TIMEOUT,
            env=env,
        )
        if read.returncode != 0:
            print(read.stdout, file=sys.stderr)
            return False, []
        listed = subprocess.run(
            ["git", "ls-files", "--unmerged"],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=COMMAND_TIMEOUT,
            env=env,
        )
        if listed.returncode != 0:
            print(listed.stdout, file=sys.stderr)
            return False, []
        paths = set()
        for line in listed.stdout.splitlines():
            _, _, path = line.partition("\t")
            if path:
                paths.add(path)
        return True, sorted(paths)
    finally:
        try:
            os.unlink(index_path)
        except OSError:
            pass


def classify_merge(head_sha, base_sha):
    """Decide whether a pull request needs the bot.

    Returns clean when GitHub can already merge the pull request itself,
    resx-only when the bot should attempt the merge, other when a human
    must resolve non-resx conflicts, or unknown when the comparison fails.
    """
    base_proc = run_git(["merge-base", head_sha, base_sha])
    if base_proc.returncode != 0:
        return "unknown", []
    ok, paths = conflicted_files(base_proc.stdout.strip(), head_sha, base_sha)
    if not ok:
        return "unknown", []
    if not paths:
        return "clean", []
    if all(is_managed_resx(path) for path in paths):
        return "resx-only", paths
    return "other", paths


def push_merge(head_ref, base_ref):
    """Merge the base branch and push only a fully clean result."""
    merge = run_git([
        "-c", "user.name=%s" % BOT_NAME,
        "-c", "user.email=%s" % BOT_EMAIL,
        "merge", "--no-ff", "--no-edit", base_ref,
    ])
    if merge.returncode != 0:
        print(merge.stdout)
        abort = run_git(["merge", "--abort"])
        if abort.returncode != 0:
            print(abort.stdout, file=sys.stderr)
        return False
    push = run_git(["push", "--quiet", "origin", "HEAD:refs/heads/" + head_ref])
    if push.returncode != 0:
        print(push.stdout, file=sys.stderr)
        return False
    return True


def eligible_pull_request(pr, repo):
    """Return a skip status, or None when the pull request is a candidate."""
    if pr.get("isDraft"):
        return "skipped-draft"
    labels = [label.get("name", "") for label in pr.get("labels", [])]
    if DEFAULT_OPT_OUT_LABEL in labels:
        return "skipped-opt-out"
    owner = (pr.get("headRepositoryOwner") or {}).get("login", "")
    name = (pr.get("headRepository") or {}).get("name", "")
    if not pr.get("headRefName") or owner + "/" + name != repo:
        return "skipped-fork"
    return None


def process_pull_request(pr, repo, base, base_sha, dry_run):
    """Handle one pull request. Returns a status word."""
    skipped = eligible_pull_request(pr, repo)
    if skipped is not None:
        return skipped
    number = pr.get("number", 0)
    head_ref = pr.get("headRefName", "")
    if run_git(["fetch", "--quiet", "origin", head_ref]).returncode != 0:
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
        print("auto_merge: #%d also conflicts outside resx: %s"
              % (number, ", ".join(paths)))
        return "conflict-skipped"
    if dry_run:
        return "would-merge"
    checkout = run_git(["checkout", "--quiet", "--detach", head_sha])
    if checkout.returncode != 0:
        print(checkout.stdout, file=sys.stderr)
        return "checkout-failed"
    if not push_merge(head_ref, "origin/" + base):
        return "conflict"
    print("auto_merge: #%d merged %s into %s" % (number, base, head_ref))
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
                handle.write("| [#%d](%s) | %s |\n" % (number, url, status))
    except OSError as exc:
        print("auto_merge: warning: cannot write step summary: %s" % exc)


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


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default=DEFAULT_BASE)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--max-prs", type=int, default=DEFAULT_MAX_PRS)
    args = parser.parse_args(argv)

    if not worktree_clean():
        return fail("worktree is not clean; commit or stash changes first")
    start_revision = current_revision()
    if start_revision is None:
        return fail("cannot read the current revision")

    repo = base_repository()
    if not repo:
        return fail("cannot determine the base repository")
    if run_git(["fetch", "--quiet", "origin", args.base]).returncode != 0:
        return fail("cannot fetch the base branch %s" % args.base)
    base_sha = resolve_revision("origin/" + args.base)
    if base_sha is None:
        return fail("cannot resolve the base branch %s" % args.base)

    prs = list_pull_requests(args.base)
    if prs is None:
        return fail("cannot list open pull requests")
    if len(prs) > args.max_prs:
        print("auto_merge: processing first %d of %d pull requests"
              % (args.max_prs, len(prs)))
        prs = prs[:args.max_prs]

    if not args.dry_run and not ensure_driver():
        return fail("cannot register the resx merge driver")

    results = []
    try:
        for pr in prs:
            status = process_pull_request(pr, repo, args.base, base_sha, args.dry_run)
            results.append((pr.get("number", 0), pr.get("url", ""), status))
            print("auto_merge: #%d %s: %s"
                  % (pr.get("number", 0), pr.get("headRefName", ""), status))
    finally:
        restore_revision(start_revision)

    write_summary(results)
    merged = sum(1 for _, _, status in results if status == "merged")
    print("auto_merge: %d merged, %d total" % (merged, len(results)))
    return 0


if __name__ == "__main__":
    sys.exit(main())

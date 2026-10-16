#!/usr/bin/env python3
r"""
scripts/git_workflow.py
Automation helper for Scoped Staging, Conventional Commits 1.0.0,
and Semantic Versioning 2.0.0 Release Tagging in E:\Accounting.
"""

import argparse
import re
import subprocess
import sys
from pathlib import Path

# Functional scope mapping rules
SCOPE_PATTERNS = {
    "repo": [r"^\.gitignore$", r"^README\.md$", r"^LICENSE(\.md)?$"],
    "backend": [r"^Backend/(?!test_).*"],
    "frontend": [r"^Frontend/.*"],
    "test": [r"^Backend/test_.*", r"^tests/.*"],
    "docs": [r"^docs/.*"],
    "agents": [r"^\.agents/.*"],
    "versioning": [r"^scripts/.*"],
}

ALLOWED_TYPES = ["feat", "fix", "chore", "docs", "test", "refactor", "style", "perf"]
ALLOWED_SCOPES = list(SCOPE_PATTERNS.keys()) + ["content-pack"]


def run_git_cmd(args, capture_output=True, check=True):
    """Run a git command and return stdout string."""
    try:
        result = subprocess.run(
            ["git"] + args,
            capture_output=capture_output,
            text=True,
            check=check
        )
        return result.stdout.rstrip("\r\n") if capture_output else ""
    except subprocess.CalledProcessError as e:
        if capture_output and e.stderr:
            sys.stderr.write(f"Git error: {e.stderr.strip()}\n")
        raise


def get_changed_files():
    """Retrieve list of modified, added, deleted, and untracked files."""
    status_output = run_git_cmd(["status", "--porcelain=v1"])
    files = []
    if not status_output:
        return files
    for line in status_output.splitlines():
        if len(line) >= 4:
            status_code = line[:2]
            filepath = line[3:].strip()
            # Handle quoted paths or renames
            if " -> " in filepath:
                filepath = filepath.split(" -> ")[1].strip()
            filepath = filepath.strip('"')
            files.append((status_code, filepath))
    return files


def classify_file(filepath):
    """Classify a filepath into a project scope."""
    for scope, patterns in SCOPE_PATTERNS.items():
        for pat in patterns:
            if re.search(pat, filepath):
                return scope
    return "other"


def cmd_status(args):
    """Group and display working tree changes by functional scope."""
    branch = run_git_cmd(["branch", "--show-current"])
    print(f"\n[Git Workflow] Current branch: {branch or '(detached HEAD)'}")
    files = get_changed_files()
    if not files:
        print("Working tree is completely clean. Nothing to stage or commit.\n")
        return

    grouped = {scope: [] for scope in ALLOWED_SCOPES + ["other"]}
    staged_count = 0
    unstaged_count = 0

    for status_code, fpath in files:
        scope = classify_file(fpath)
        is_staged = status_code[0] not in (" ", "?")
        if is_staged:
            staged_count += 1
        else:
            unstaged_count += 1
        grouped[scope].append((status_code, fpath, is_staged))

    print(f"Summary: {staged_count} staged, {unstaged_count} unstaged/untracked file(s)\n")
    print(f"{'SCOPE':<12} {'STATUS':<8} {'FILE PATH'}")
    print("-" * 60)
    for scope in ALLOWED_SCOPES + ["other"]:
        items = grouped[scope]
        if items:
            for status_code, fpath, is_staged in items:
                stage_indicator = "[STAGED]" if is_staged else "[UNSTAGED]"
                print(f"{scope:<12} {stage_indicator:<10} {fpath}")
    print("")


def cmd_add_scope(args):
    """Stage all files belonging to a specific scope."""
    scope = args.scope
    if scope not in ALLOWED_SCOPES:
        sys.stderr.write(f"Error: Invalid scope '{scope}'. Allowed: {', '.join(ALLOWED_SCOPES)}\n")
        sys.exit(1)

    files = get_changed_files()
    matching_files = [fpath for _, fpath in files if classify_file(fpath) == scope]

    if not matching_files:
        print(f"No unstaged or modified files found for scope '{scope}'.")
        return

    print(f"Staging {len(matching_files)} file(s) for scope '{scope}':")
    for f in matching_files:
        print(f"  + {f}")
        run_git_cmd(["add", f])
    print(f"Successfully staged scope '{scope}'.")


def cmd_check_secrets(args):
    """Check staged files for forbidden secrets, credentials, or binaries."""
    staged_output = run_git_cmd(["diff", "--cached", "--name-only"])
    if not staged_output:
        print("No files currently staged.")
        return

    forbidden_patterns = [
        r"\.env($|\..*)",
        r".*\.db$",
        r".*\.sqlite[3]?$",
        r".*secret.*",
        r".*id_rsa.*",
        r".*password.*"
    ]
    violations = []
    for fpath in staged_output.splitlines():
        for pat in forbidden_patterns:
            if re.search(pat, fpath, re.IGNORECASE) and not fpath.endswith(".example"):
                violations.append((fpath, pat))

    if violations:
        sys.stderr.write("\n[SECURITY ALERT] Staged files contain potential secrets or binary databases:\n")
        for fpath, pat in violations:
            sys.stderr.write(f"  - {fpath} (matched rule: {pat})\n")
        sys.stderr.write("Commit aborted. Unstage these files with 'git reset HEAD <file>'.\n\n")
        sys.exit(1)
    else:
        print("[Security Check Passed] No sensitive credentials or database binaries staged.")


def cmd_commit(args):
    """Validate Conventional Commit format, check secrets, and commit staged changes."""
    # Run secret check first
    cmd_check_secrets(args)

    staged_output = run_git_cmd(["diff", "--cached", "--name-only"])
    if not staged_output:
        sys.stderr.write("Error: No changes are staged. Use 'git add <files>' or 'python scripts/git_workflow.py add --scope <scope>' first.\n")
        sys.exit(1)

    c_type = args.type.lower()
    c_scope = args.scope.lower()
    c_desc = args.message.strip()

    if c_type not in ALLOWED_TYPES:
        sys.stderr.write(f"Error: Invalid commit type '{c_type}'. Allowed: {', '.join(ALLOWED_TYPES)}\n")
        sys.exit(1)

    if c_scope not in ALLOWED_SCOPES:
        sys.stderr.write(f"Error: Invalid commit scope '{c_scope}'. Allowed: {', '.join(ALLOWED_SCOPES)}\n")
        sys.exit(1)

    if not c_desc:
        sys.stderr.write("Error: Commit description cannot be empty.\n")
        sys.exit(1)

    commit_title = f"{c_type}({c_scope}): {c_desc}"
    print(f"\nCommitting with Conventional Commit: {commit_title}")
    run_git_cmd(["commit", "-m", commit_title], capture_output=False)
    print("\nCommit successfully created!")


def cmd_tag(args):
    """Create an annotated Semantic Versioning 2.0.0 release tag."""
    tag_name = args.version.strip()
    semver_pattern = r"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$"
    if not re.match(semver_pattern, tag_name):
        sys.stderr.write(f"Error: '{tag_name}' does not conform to Semantic Versioning 2.0.0 (expected e.g. v0.1.0, v1.0.0).\n")
        sys.exit(1)

    # Normalize to include leading 'v'
    if not tag_name.startswith("v"):
        tag_name = "v" + tag_name

    existing_tags = run_git_cmd(["tag", "-l"]).splitlines()
    if tag_name in existing_tags:
        sys.stderr.write(f"Error: Tag '{tag_name}' already exists in this repository.\n")
        sys.exit(1)

    message = args.message or f"Release {tag_name}"
    run_git_cmd(["tag", "-a", tag_name, "-m", message])
    print(f"Successfully created release tag: {tag_name} ('{message}')")


def main():
    parser = argparse.ArgumentParser(description="Git workflow & SemVer tagging automation for Accounting repository.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    # status
    p_status = subparsers.add_parser("status", help="Inspect repository status grouped by functional scope")
    p_status.set_defaults(func=cmd_status)

    # add
    p_add = subparsers.add_parser("add", help="Stage all files for a specific scope")
    p_add.add_argument("--scope", required=True, choices=ALLOWED_SCOPES, help="Functional scope to stage")
    p_add.set_defaults(func=cmd_add_scope)

    # check-secrets
    p_sec = subparsers.add_parser("check-secrets", help="Check staged files for forbidden credentials or binaries")
    p_sec.set_defaults(func=cmd_check_secrets)

    # commit
    p_commit = subparsers.add_parser("commit", help="Commit staged changes using Conventional Commits 1.0.0")
    p_commit.add_argument("--type", required=True, choices=ALLOWED_TYPES, help="Commit type (feat, fix, chore, etc.)")
    p_commit.add_argument("--scope", required=True, choices=ALLOWED_SCOPES, help="Functional scope")
    p_commit.add_argument("-m", "--message", required=True, help="Commit description")
    p_commit.set_defaults(func=cmd_commit)

    # tag
    p_tag = subparsers.add_parser("tag", help="Create an annotated SemVer 2.0.0 release tag")
    p_tag.add_argument("version", help="Version string (e.g. v0.1.0)")
    p_tag.add_argument("-m", "--message", help="Annotation message for the release tag")
    p_tag.set_defaults(func=cmd_tag)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()

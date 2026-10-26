#!/usr/bin/env python3
r"""
scripts/git_workflow.py
Automation helper for Scoped Staging, Conventional Commits 1.0.0,
and Semantic Versioning 2.0.0 Release Tagging in E:\Accounting.
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent

# Functional scope mapping rules
SCOPE_PATTERNS = {
    "repo": [
        r"^\.gitignore$",
        r"^\.env\.example$",
        r"^README\.md$",
        r"^LICENSE(\.md)?$",
        r"^package\.json$",
        r"^VERSION$",
        r"^requirements\.txt$",
        r"^runtime\.txt$",
        r"^Procfile$",
        r"^nginx/.*",
    ],
    "backend": [r"^Backend/(?!test_).*", r"^Backend\.DotNet/.*", r"^db/.*", r"^scaffold\.(sh|ps1)$"],
    "frontend": [r"^Frontend/.*"],
    "test": [r"^Backend/test_.*", r"^tests/.*"],
    "docs": [r"^docs/.*", r"^INSTRUCTIONS\.txt$"],
    "agents": [r"^\.agents/.*"],
    "versioning": [r"^scripts/.*"],
}

ALLOWED_TYPES = ["feat", "fix", "chore", "docs", "test", "refactor", "style", "perf"]
ALLOWED_SCOPES = list(SCOPE_PATTERNS.keys()) + ["content-pack"]

SEMVER_REGEX = re.compile(
    r"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$"
)


def run_git_cmd(args, capture_output=True, check=True):
    """Run a git command in ROOT_DIR and return stdout string."""
    try:
        result = subprocess.run(
            ["git"] + args,
            cwd=str(ROOT_DIR),
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


def parse_semver(tag_str):
    """Parse tag_str into (major, minor, patch, prerelease, build) or None."""
    m = SEMVER_REGEX.match(tag_str.strip())
    if not m:
        return None
    major, minor, patch = int(m.group(1)), int(m.group(2)), int(m.group(3))
    prerelease = m.group(4) or ""
    build = m.group(5) or ""
    return (major, minor, patch, prerelease, build)


def get_all_semver_tags():
    """Retrieve and semantically sort all git tags."""
    try:
        tags = run_git_cmd(["tag", "-l"]).splitlines()
    except Exception:
        return []
    semver_tags = []
    for t in tags:
        parsed = parse_semver(t)
        if parsed:
            semver_tags.append((parsed, t))
    # Sort strictly by (major, minor, patch)
    semver_tags.sort(key=lambda x: (x[0][0], x[0][1], x[0][2]))
    return [t[1] for t in semver_tags]


def get_latest_semver():
    """Return the highest SemVer tag in the repository."""
    sorted_tags = get_all_semver_tags()
    if not sorted_tags:
        return "v0.1.0"
    return sorted_tags[-1]


def calculate_next_version(current_tag, bump_type="patch"):
    """
    Calculate the next semantic version string given current_tag and bump_type.
    bump_type must be 'patch', 'minor', or 'major'.
    """
    parsed = parse_semver(current_tag)
    if not parsed:
        parsed = (0, 1, 0, "", "")
    major, minor, patch = parsed[0], parsed[1], parsed[2]
    if bump_type == "patch":
        patch += 1
    elif bump_type == "minor":
        minor += 1
        patch = 0
    elif bump_type == "major":
        major += 1
        minor = 0
        patch = 0
    else:
        raise ValueError(f"Unknown bump type '{bump_type}'. Allowed: patch, minor, major")
    return f"v{major}.{minor}.{patch}"


def sync_version_files(version_str):
    """
    Synchronize version number (without leading 'v') across:
    1. Root package.json
    2. Frontend/package.json
    3. VERSION file
    """
    clean_version = version_str.lstrip("v")
    modified_files = []

    # 1. Root package.json
    root_pkg = ROOT_DIR / "package.json"
    if root_pkg.exists():
        with open(root_pkg, "r", encoding="utf-8") as f:
            data = json.load(f)
        if data.get("version") != clean_version:
            data["version"] = clean_version
            with open(root_pkg, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
                f.write("\n")
            modified_files.append("package.json")

    # 2. Frontend/package.json
    front_pkg = ROOT_DIR / "Frontend" / "package.json"
    if front_pkg.exists():
        with open(front_pkg, "r", encoding="utf-8") as f:
            data = json.load(f)
        if data.get("version") != clean_version:
            data["version"] = clean_version
            with open(front_pkg, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
                f.write("\n")
            modified_files.append("Frontend/package.json")

    # 3. VERSION file
    version_file = ROOT_DIR / "VERSION"
    existing_v = version_file.read_text(encoding="utf-8").strip() if version_file.exists() else ""
    if existing_v != clean_version:
        version_file.write_text(f"{clean_version}\n", encoding="utf-8")
        modified_files.append("VERSION")

    return modified_files


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
            if re.search(pat, fpath, re.IGNORECASE) and not fpath.endswith((".example", ".cs")):
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


def cmd_version(args):
    """Display current release version and projected increments."""
    latest = get_latest_semver()
    next_patch = calculate_next_version(latest, "patch")
    next_minor = calculate_next_version(latest, "minor")
    next_major = calculate_next_version(latest, "major")

    print("\n=== R.G.E Accounting SemVer Information ===")
    print(f"Latest Git Release Tag: {latest}")
    print(f"Next Patch Release:     {next_patch}  (Bug fixes, resilience patches, non-breaking fixes)")
    print(f"Next Minor Release:     {next_minor}  (New features, backward-compatible enhancements)")
    print(f"Next Major Release:     {next_major}  (Breaking architectural or schema upgrades)")

    root_pkg = ROOT_DIR / "package.json"
    front_pkg = ROOT_DIR / "Frontend" / "package.json"
    ver_file = ROOT_DIR / "VERSION"

    print("\nSynchronized Package Versions:")
    if root_pkg.exists():
        with open(root_pkg, "r", encoding="utf-8") as f:
            print(f"  package.json:          {json.load(f).get('version')}")
    if front_pkg.exists():
        with open(front_pkg, "r", encoding="utf-8") as f:
            print(f"  Frontend/package.json: {json.load(f).get('version')}")
    if ver_file.exists():
        print(f"  VERSION:               {ver_file.read_text(encoding='utf-8').strip()}")
    print("")


def cmd_bump(args):
    """Bump the semantic version in package.json, Frontend/package.json, and VERSION."""
    latest = get_latest_semver()
    if args.version:
        new_version = args.version.strip()
        if not new_version.startswith("v"):
            new_version = "v" + new_version
        if not parse_semver(new_version):
            sys.stderr.write(f"Error: '{new_version}' is not a valid Semantic Version (e.g. v0.9.31).\n")
            sys.exit(1)
    else:
        new_version = calculate_next_version(latest, args.type)

    print(f"\nBumping version from {latest} -> {new_version}...")
    modified = sync_version_files(new_version)
    if modified:
        print("Updated version files:")
        for m in modified:
            print(f"  + {m}")
    else:
        print("All version files already match target version.")

    if args.stage:
        for m in modified:
            run_git_cmd(["add", m])
        print("Staged updated version files.")


def cmd_tag(args):
    """Create an annotated Semantic Versioning 2.0.0 release tag."""
    tag_name = args.version.strip()
    if not parse_semver(tag_name):
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


def cmd_release(args):
    """
    Automate the full release cycle:
    1. Calculate next version (or use explicit version)
    2. Synchronize version files (package.json, Frontend/package.json, VERSION)
    3. Stage version files
    4. Commit using Conventional Commits chore(repo): release <version>: <message>
    5. Create annotated release tag
    6. Optionally push branch and tags to remote
    """
    latest = get_latest_semver()
    if args.version:
        new_version = args.version.strip()
        if not new_version.startswith("v"):
            new_version = "v" + new_version
        if not parse_semver(new_version):
            sys.stderr.write(f"Error: '{new_version}' is not a valid Semantic Version.\n")
            sys.exit(1)
    else:
        new_version = calculate_next_version(latest, args.type)

    release_desc = args.message or f"Release {new_version}"
    print(f"\n=== Executing Automated Release: {latest} -> {new_version} ===")

    # 1. Sync files
    modified = sync_version_files(new_version)
    if modified:
        print("Synchronized version files:")
        for m in modified:
            print(f"  + {m}")
            run_git_cmd(["add", m])
        print("Staged version files under scope 'repo'.")

    # 2. Check if there are staged changes to commit
    staged = run_git_cmd(["diff", "--cached", "--name-only"]).splitlines()
    if staged:
        commit_msg = f"release {new_version}: {release_desc}"
        print(f"\nCommitting: chore(repo): {commit_msg}")
        run_git_cmd(["commit", "-m", f"chore(repo): {commit_msg}"], capture_output=False)
    else:
        print("No staged changes to commit; proceeding to tag creation.")

    # 3. Create tag
    existing_tags = run_git_cmd(["tag", "-l"]).splitlines()
    if new_version in existing_tags:
        print(f"Tag '{new_version}' already exists.")
    else:
        run_git_cmd(["tag", "-a", new_version, "-m", f"Release {new_version}: {release_desc}"])
        print(f"Successfully created release tag: {new_version}")

    # 4. Optional Push
    if args.push:
        print(f"\nPushing main branch and tags to origin...")
        run_git_cmd(["push", "origin", "main", "--tags"], capture_output=False)
        print("Successfully published release to origin!")

    print(f"\nRelease {new_version} successfully completed!\n")


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

    # version
    p_ver = subparsers.add_parser("version", help="Display current version and projected semantic increments")
    p_ver.set_defaults(func=cmd_version)

    # bump
    p_bump = subparsers.add_parser("bump", help="Bump version across package files without tagging")
    p_bump.add_argument("--type", choices=["patch", "minor", "major"], default="patch", help="Semantic bump type")
    p_bump.add_argument("-v", "--version", help="Explicit version string override")
    p_bump.add_argument("--stage", action="store_true", help="Automatically stage modified version files")
    p_bump.set_defaults(func=cmd_bump)

    # tag
    p_tag = subparsers.add_parser("tag", help="Create an annotated SemVer 2.0.0 release tag manually")
    p_tag.add_argument("version", help="Version string (e.g. v0.1.0)")
    p_tag.add_argument("-m", "--message", help="Annotation message for the release tag")
    p_tag.set_defaults(func=cmd_tag)

    # release
    p_rel = subparsers.add_parser("release", help="Automated release pipeline (calculate, sync files, commit, tag, push)")
    p_rel.add_argument("--type", choices=["patch", "minor", "major"], default="patch", help="Semantic bump type (default: patch)")
    p_rel.add_argument("-v", "--version", help="Explicit version string override (e.g. v1.0.0)")
    p_rel.add_argument("-m", "--message", help="Annotation message for the release")
    p_rel.add_argument("--push", action="store_true", help="Push commit and tags to origin after release")
    p_rel.set_defaults(func=cmd_release)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()

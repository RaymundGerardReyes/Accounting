#!/usr/bin/env python3
r"""
scripts/git_workflow.py
Automation helper for Scoped Staging, Conventional Commits 1.0.0,
and Semantic Versioning 2.0.0 Release Tagging in Accounting-Checkout-test.
"""

import argparse
import json
import re
import subprocess
import sys
import time
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent

# Primary classification rules (ordered from most specific to broadest)
PRIMARY_SCOPE_PATTERNS = [
    ("agents", [r"^\.agents/.*"]),
    ("migration", [r"^docs/migration/.*", r"^tools/migration/.*"]),
    ("versioning", [
        r"^scripts/git_workflow\.py$",
        r"^tools/versioning/.*",
        r"^commitlint\.config\.js$",
        r"^\.githooks/.*",
        r"^\.frontend-drift-allowlist$",
        r"^\.release-line$",
    ]),
    ("release", [r"^docs/release/.*", r"^docs/runbooks/.*", r"^\.github/workflows/.*"]),
    ("deploy", [
        r"^Backend/deploy/.*",
        r"^deploy/.*",
        r"^nginx/.*",
        r"^Procfile$",
        r"^project\.toml$",
    ]),
    ("db", [r"^Backend/db/.*", r"^db/.*"]),
    ("test", [
        r"^Backend/tests/.*",
        r"^Backend/test_.*",
        r"^tests/.*",
        r"^Frontend/.*\.test\.(ts|tsx|js|jsx)$",
        r"^Frontend/scripts/test-.*",
        r"^Frontend/vitest\..*",
        r"^TESTING_QA_PROMPT\.md$",
    ]),
    ("docs", [
        r"^docs/.*",
        r"^Backend/docs/.*",
        r"^INSTRUCTIONS\.txt$",
        r"^Backend/README\.md$",
        r"^scripts/google_apps_script/README\.md$",
    ]),
    ("payments", [
        r"^Backend/src/Modules/Payments/.*",
        r"^scripts/Test-PayMongoWebhookLocal\.ps1$",
    ]),
    ("receipts", [r"^Backend/src/Modules/Receipts/.*"]),
    ("finance", [r"^Backend/src/Modules/Finance/.*"]),
    ("identity", [r"^Backend/src/Modules/Identity/.*"]),
    ("audit", [
        r"^Backend/src/Modules/Audit/.*",
        r"^Backend/tools/AuditChain\.Verifier/.*",
    ]),
    ("costalloc", [r"^Backend/src/Modules/CostAllocation/.*"]),
    ("documents", [r"^Backend/src/Modules/Documents/.*"]),
    ("integrations", [
        r"^Backend/src/Modules/Integrations/.*",
        r"^scripts/google_apps_script/.*",
    ]),
    ("dotnet", [
        r"^Backend/.*",
        r"^Rge\.Accounting\.Host\.slnx$",
        r"^global\.json$",
    ]),
    ("backend", [
        r"^Backend/.*",
        r"^scripts/start_production\.py$",
        r"^scaffold\.(sh|ps1)$",
    ]),
    ("frontend", [r"^Frontend/.*"]),
    ("repo", [
        r"^\.gitignore$",
        r"^\.env\.example$",
        r"^README\.md$",
        r"^LICENSE(\.md)?$",
        r"^package\.json$",
        r"^VERSION$",
        r"^requirements\.txt$",
        r"^runtime\.txt$",
    ]),
]

# Broad alias scopes so `add --scope dotnet` or `add --scope backend` stages all relevant files
BROAD_SCOPE_MATCHERS = {
    "dotnet": [
        r"^Backend/(?!tests/).*",
        r"^Rge\.Accounting\.Host\.slnx$",
        r"^global\.json$",
        r"^project\.toml$",
    ],
    "backend": [
        r"^Backend/(?!tests/).*",
        r"^db/.*",
        r"^scripts/google_apps_script/.*",
        r"^scripts/start_production\.py$",
        r"^scaffold\.(sh|ps1)$",
    ],
    "web": [r"^Frontend/.*"],
    "deps": [
        r".*packages\.lock\.json$",
        r".*package-lock\.json$",
        r".*pnpm-lock\.yaml$",
        r"^Directory\.Packages\.props$",
        r"^Backend/Directory\.Packages\.props$",
        r"^requirements\.txt$",
    ],
}

ALLOWED_TYPES = [
    "feat", "fix", "chore", "docs", "test", "refactor",
    "style", "perf", "build", "ci", "revert"
]

ALLOWED_SCOPES = [
    # Core Monorepo Scopes
    "repo", "backend", "dotnet", "frontend", "test", "docs",
    "agents", "versioning", "migration", "content-pack",
    # Domain Module Scopes
    "web", "payments", "receipts", "finance", "identity",
    "audit", "costalloc", "documents", "integrations",
    "db", "deploy", "release", "deps"
]

SEMVER_REGEX = re.compile(
    r"^(?:(dotnet|python)/)?v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?"
    r"(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$"
)

# Safe source/doc extensions that may contain the words "secret" or "password" in their filename
SAFE_CODE_OR_DOC_EXTENSIONS = (
    ".example", ".cs", ".ts", ".tsx", ".js", ".cjs", ".mjs",
    ".py", ".ps1", ".sh", ".gs", ".md", ".txt", ".yaml", ".yml",
    ".sql", ".json", ".html", ".css"
)


def run_git_cmd(args, capture_output=True, check=True):
    """Run a git command in ROOT_DIR and return stdout string."""
    try:
        result = subprocess.run(
            ["git"] + args,
            cwd=str(ROOT_DIR),
            capture_output=capture_output,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=check,
        )
        return result.stdout.rstrip("\r\n") if capture_output else ""
    except subprocess.CalledProcessError as e:
        if capture_output and e.stderr:
            sys.stderr.write(f"Git error: {e.stderr.strip()}\n")
        raise


def get_release_line():
    """Determine the active technology line from .release-line or Git branch."""
    line_file = ROOT_DIR / ".release-line"
    if line_file.exists():
        val = line_file.read_text(encoding="utf-8").strip().lower()
        if val in ("dotnet", "python"):
            return val
    try:
        branch = run_git_cmd(["branch", "--show-current"]).lower()
        if "python" in branch:
            return "python"
        return "dotnet"
    except Exception:
        return "dotnet"


def get_changed_files():
    """Retrieve list of modified, added, deleted, and untracked files (expanding untracked dirs)."""
    status_output = run_git_cmd(["status", "--porcelain=v1", "-uall"])
    files = []
    if not status_output:
        return files
    for line in status_output.splitlines():
        if len(line) >= 4:
            status_code = line[:2]
            filepath = line[3:].strip()
            if " -> " in filepath:
                filepath = filepath.split(" -> ", 1)[1].strip()
            filepath = filepath.strip('"').replace("\\", "/")
            files.append((status_code, filepath))
    return files


def classify_file(filepath):
    """Classify a filepath into its most specific primary project scope."""
    norm_path = filepath.replace("\\", "/")
    for scope, patterns in PRIMARY_SCOPE_PATTERNS:
        for pat in patterns:
            if re.search(pat, norm_path):
                return scope
    return "other"


def file_matches_scope(filepath, target_scope):
    """Return True if filepath belongs to target_scope (either primary or broad alias scope)."""
    norm_path = filepath.replace("\\", "/")
    if classify_file(norm_path) == target_scope:
        return True
    for pat in BROAD_SCOPE_MATCHERS.get(target_scope, []):
        if re.search(pat, norm_path):
            return True
    return False


def normalize_semver_tag(tag_str, line=None):
    """
    Normalize a SemVer tag string and preserve the active release line prefix
    (e.g. '0.5.18' -> 'dotnet/v0.5.18', 'dotnet/0.5.18' -> 'dotnet/v0.5.18').
    """
    tag_str = tag_str.strip()
    if "/" in tag_str:
        prefix, ver = tag_str.split("/", 1)
        if not ver.startswith("v"):
            ver = "v" + ver
        return f"{prefix}/{ver}"

    active_line = line if line is not None else get_release_line()
    ver = tag_str if tag_str.startswith("v") else f"v{tag_str}"
    return f"{active_line}/{ver}" if active_line else ver


def parse_semver(tag_str):
    """Parse tag_str into (prefix, major, minor, patch, prerelease, build) or None."""
    m = SEMVER_REGEX.match(tag_str.strip())
    if not m:
        return None
    prefix = m.group(1) or ""
    major, minor, patch = int(m.group(2)), int(m.group(3)), int(m.group(4))
    prerelease = m.group(5) or ""
    build = m.group(6) or ""
    return (prefix, major, minor, patch, prerelease, build)


def _semver_sort_key(parsed):
    """
    SemVer 2.0.0 precedence sort key:
    Normal releases (prerelease == '') sort AFTER pre-releases of the same major.minor.patch.
    """
    _, major, minor, patch, prerelease, _ = parsed
    is_stable = 1 if prerelease == "" else 0
    return (major, minor, patch, is_stable, prerelease)


def get_all_semver_tags(line=None):
    """Retrieve and semantically sort all git tags for the active release line."""
    if line is None:
        line = get_release_line()
    try:
        tags = run_git_cmd(["tag", "-l"]).splitlines()
    except Exception:
        tags = []

    semver_tags = []
    target_prefix = f"{line}/v"
    for t in tags:
        t_clean = t.strip()
        if t_clean.startswith(target_prefix):
            parsed = parse_semver(t_clean)
            if parsed:
                semver_tags.append((parsed, t_clean))
        elif not line and "/" not in t_clean:
            parsed = parse_semver(t_clean)
            if parsed:
                semver_tags.append((parsed, t_clean))

    # Fallback to legacy unprefixed tags if no line-prefixed tags exist
    if not semver_tags:
        for t in tags:
            t_clean = t.strip()
            if "/" not in t_clean:
                parsed = parse_semver(t_clean)
                if parsed:
                    semver_tags.append((parsed, t_clean))

    # Also check root VERSION file so we never regress below the committed VERSION
    version_file = ROOT_DIR / "VERSION"
    if version_file.exists():
        raw_ver = version_file.read_text(encoding="utf-8").strip()
        candidate_tag = f"{line}/v{raw_ver.lstrip('v')}" if line else f"v{raw_ver.lstrip('v')}"
        parsed_ver = parse_semver(candidate_tag)
        if parsed_ver and not any(t[1] == candidate_tag for t in semver_tags):
            semver_tags.append((parsed_ver, candidate_tag))

    semver_tags.sort(key=lambda x: _semver_sort_key(x[0]))
    return [t[1] for t in semver_tags]


def get_latest_semver(line=None):
    """Return the highest SemVer tag in the repository for the active line."""
    if line is None:
        line = get_release_line()
    sorted_tags = get_all_semver_tags(line)
    if not sorted_tags:
        return f"{line}/v0.1.0" if line else "v0.1.0"
    return sorted_tags[-1]


def calculate_next_version(current_tag, bump_type="patch"):
    """
    Calculate the next semantic version string given current_tag and bump_type.
    Preserves tag prefix (e.g. dotnet/v or python/v).
    """
    parsed = parse_semver(current_tag)
    line = get_release_line()
    if parsed:
        effective_line = parsed[0] or line
        prefix = f"{effective_line}/v" if effective_line else "v"
        major, minor, patch = parsed[1], parsed[2], parsed[3]
    else:
        prefix = f"{line}/v" if line else "v"
        major, minor, patch = 0, 1, 0

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
    return f"{prefix}{major}.{minor}.{patch}"


def sync_version_files(version_str):
    """
    Synchronize version number across:
    1. VERSION file (raw SemVer e.g. 0.5.18)
    2. Frontend/.env.production.local (untracked local build metadata)
    Note: package.json and Frontend/package.json STRICTLY remain '0.0.0-managed' per ADR 0011.
    """
    clean_version = version_str.strip()
    if "/" in clean_version:
        clean_version = clean_version.split("/", 1)[1]
    clean_version = clean_version.lstrip("v")

    modified_files = []

    # 1. VERSION file
    version_file = ROOT_DIR / "VERSION"
    existing_v = version_file.read_text(encoding="utf-8").strip() if version_file.exists() else ""
    if existing_v != clean_version:
        version_file.write_text(f"{clean_version}\n", encoding="utf-8")
        modified_files.append("VERSION")

    # 2. Verify package.json preserves 0.0.0-managed (ADR 0011 invariant)
    for pkg_path in [ROOT_DIR / "package.json", ROOT_DIR / "Frontend" / "package.json"]:
        if pkg_path.exists():
            with open(pkg_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            if data.get("version") != "0.0.0-managed":
                data["version"] = "0.0.0-managed"
                with open(pkg_path, "w", encoding="utf-8") as f:
                    json.dump(data, f, indent=2)
                    f.write("\n")
                modified_files.append(str(pkg_path.relative_to(ROOT_DIR)).replace("\\", "/"))

    # 3. Generate Frontend/.env.production.local (not git-staged)
    try:
        line = get_release_line()
        git_sha = run_git_cmd(["rev-parse", "--short", "HEAD"])
        build_time = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        env_content = (
            f"NEXT_PUBLIC_APP_LINE={line}\n"
            f"NEXT_PUBLIC_APP_VERSION={clean_version}\n"
            f"NEXT_PUBLIC_GIT_SHA={git_sha}\n"
            f"NEXT_PUBLIC_API_CONTRACT=v1\n"
            f"NEXT_PUBLIC_BUILD_TIME={build_time}\n"
        )
        env_local = ROOT_DIR / "Frontend" / ".env.production.local"
        if env_local.parent.exists():
            env_local.write_text(env_content, encoding="utf-8")
    except Exception:
        pass

    return modified_files


def cmd_status(args):
    """Group and display working tree changes by functional scope."""
    branch = run_git_cmd(["branch", "--show-current"])
    line = get_release_line()
    print(f"\n[Git Workflow] Current branch: {branch or '(detached HEAD)'} | Release line: {line}")
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
    print(f"{'SCOPE':<14} {'STATUS':<10} {'FILE PATH'}")
    print("-" * 72)
    for scope in ALLOWED_SCOPES + ["other"]:
        items = grouped[scope]
        if items:
            for status_code, fpath, is_staged in items:
                stage_indicator = "[STAGED]" if is_staged else "[UNSTAGED]"
                print(f"{scope:<14} {stage_indicator:<10} {fpath}")
    print("")


def cmd_add_scope(args):
    """Stage all files belonging to a specific scope."""
    scope = args.scope.lower()
    if scope not in ALLOWED_SCOPES:
        sys.stderr.write(f"Error: Invalid scope '{scope}'. Allowed: {', '.join(ALLOWED_SCOPES)}\n")
        sys.exit(1)

    files = get_changed_files()
    matching_files = [fpath for _, fpath in files if file_matches_scope(fpath, scope)]

    if not matching_files:
        print(f"No unstaged or modified files found for scope '{scope}'.")
        return

    print(f"Staging {len(matching_files)} file(s) for scope '{scope}':")
    for f in matching_files:
        print(f"  + {f}")
        run_git_cmd(["add", "--", f])
    print(f"Successfully staged scope '{scope}'.")


def cmd_check_secrets(args):
    """Check staged files for forbidden secrets, env files, private keys, or database binaries."""
    staged_output = run_git_cmd(["diff", "--cached", "--name-only"])
    if not staged_output:
        print("No files currently staged.")
        return

    strict_forbidden_patterns = [
        (r"(^|/)\.env($|\.(?!example$).*)", "Environment secret file (.env)"),
        (r".*\.(db|sqlite|sqlite3)$", "SQLite/Database binary file"),
        (r".*(id_rsa|id_ed25519|\.pem|\.pfx|\.p12|\.key)$", "Private key / certificate file"),
    ]
    keyword_forbidden_patterns = [
        (r".*secret.*", "Filename contains 'secret'"),
        (r".*password.*", "Filename contains 'password'"),
    ]

    violations = []
    for raw_path in staged_output.splitlines():
        fpath = raw_path.strip().replace("\\", "/")
        if not fpath:
            continue

        for pat, reason in strict_forbidden_patterns:
            if re.search(pat, fpath, re.IGNORECASE):
                violations.append((fpath, reason))

        if not fpath.lower().endswith(SAFE_CODE_OR_DOC_EXTENSIONS):
            for pat, reason in keyword_forbidden_patterns:
                if re.search(pat, fpath, re.IGNORECASE):
                    violations.append((fpath, reason))

    if violations:
        sys.stderr.write("\n[SECURITY ALERT] Staged files contain potential secrets or binary databases:\n")
        for fpath, reason in violations:
            sys.stderr.write(f"  - {fpath} ({reason})\n")
        sys.stderr.write("Commit aborted. Unstage these files with 'git reset HEAD <file>'.\n\n")
        sys.exit(1)
    else:
        print("[Security Check Passed] No sensitive credentials or database binaries staged.")


def cmd_commit(args):
    """Validate Conventional Commit format, check secrets, and commit staged changes."""
    cmd_check_secrets(args)

    staged_output = run_git_cmd(["diff", "--cached", "--name-only"])
    if not staged_output:
        sys.stderr.write(
            "Error: No changes are staged. Use 'git add <files>' or "
            "'python scripts/git_workflow.py add --scope <scope>' first.\n"
        )
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
    """Display current release version and projected increments for the active technology line."""
    line = get_release_line()
    latest = get_latest_semver(line)
    next_patch = calculate_next_version(latest, "patch")
    next_minor = calculate_next_version(latest, "minor")
    next_major = calculate_next_version(latest, "major")

    print(f"\n=== R.G.E Accounting SemVer Information (Line: {line}) ===")
    print(f"Active Release Line:    {line}")
    print(f"Latest Git Release Tag: {latest}")
    print(f"Next Patch Release:     {next_patch}  (Bug fixes, resilience patches, non-breaking fixes)")
    print(f"Next Minor Release:     {next_minor}  (Milestones, new capabilities, backward-compatible enhancements)")
    print(f"Next Major Release:     {next_major}  (Production gate, breaking architectural/schema upgrades)")

    root_pkg = ROOT_DIR / "package.json"
    front_pkg = ROOT_DIR / "Frontend" / "package.json"
    ver_file = ROOT_DIR / "VERSION"
    env_local = ROOT_DIR / "Frontend" / ".env.production.local"

    print("\nSynchronized Package Versions (ADR 0011 Invariant):")
    if root_pkg.exists():
        with open(root_pkg, "r", encoding="utf-8") as f:
            print(f"  package.json:          {json.load(f).get('version')}")
    if front_pkg.exists():
        with open(front_pkg, "r", encoding="utf-8") as f:
            print(f"  Frontend/package.json: {json.load(f).get('version')}")
    if ver_file.exists():
        print(f"  VERSION:               {ver_file.read_text(encoding='utf-8').strip()}")
    if env_local.exists():
        print("\nActive Injected Build Environment (.env.production.local):")
        for line_str in env_local.read_text(encoding="utf-8-sig").splitlines():
            if line_str.strip():
                print(f"  {line_str.strip()}")
    print("")


def cmd_bump(args):
    """Bump the semantic version in VERSION and verify package.json ADR 0011 invariants."""
    line = get_release_line()
    latest = get_latest_semver(line)
    if args.version:
        new_version = normalize_semver_tag(args.version, line)
        if not parse_semver(new_version):
            sys.stderr.write(f"Error: '{new_version}' is not a valid Semantic Version (e.g. dotnet/v0.5.18).\n")
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
            run_git_cmd(["add", "--", m])
        print("Staged updated version files.")


def cmd_tag(args):
    """Create an annotated Semantic Versioning 2.0.0 release tag."""
    line = get_release_line()
    tag_name = normalize_semver_tag(args.version, line)
    if not parse_semver(tag_name):
        sys.stderr.write(
            f"Error: '{tag_name}' does not conform to Semantic Versioning 2.0.0 "
            f"(expected e.g. {line}/v0.5.18).\n"
        )
        sys.exit(1)

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
    2. Synchronize version files (VERSION, package.json invariant)
    3. Stage version files
    4. Commit using Conventional Commits chore(repo): release <version>: <message>
    5. Create annotated release tag
    6. Optionally push branch and new tag to remote
    """
    line = get_release_line()
    latest = get_latest_semver(line)
    if args.version:
        new_version = normalize_semver_tag(args.version, line)
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
            run_git_cmd(["add", "--", m])
        print("Staged version files under scope 'repo'.")

    # 2. Commit staged changes if any exist
    staged = [s for s in run_git_cmd(["diff", "--cached", "--name-only"]).splitlines() if s.strip()]
    if staged:
        cmd_check_secrets(args)
        commit_msg = f"release {new_version}: {release_desc}"
        print(f"\nCommitting: chore(repo): {commit_msg}")
        run_git_cmd(["commit", "-m", f"chore(repo): {commit_msg}"], capture_output=False)
    else:
        print("No staged changes to commit; proceeding to tag creation.")

    # 3. Create annotated tag
    existing_tags = run_git_cmd(["tag", "-l"]).splitlines()
    if new_version in existing_tags:
        print(f"Tag '{new_version}' already exists.")
    else:
        run_git_cmd(["tag", "-a", new_version, "-m", f"Release {new_version}: {release_desc}"])
        print(f"Successfully created release tag: {new_version}")

    # 4. Optional Push (pushes current branch and the specific release tag)
    if args.push:
        current_branch = run_git_cmd(["branch", "--show-current"]).strip() or "main"
        print(f"\nPushing {current_branch} branch and tag {new_version} to origin...")
        run_git_cmd(["push", "origin", current_branch, f"refs/tags/{new_version}"], capture_output=False)
        print("Successfully published release to origin!")

    print(f"\nRelease {new_version} successfully completed!\n")


def main():
    parser = argparse.ArgumentParser(
        description="Git workflow & SemVer tagging automation for Accounting repository."
    )
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
    p_tag.add_argument("version", help="Version string (e.g. v0.5.18 or dotnet/v0.5.18)")
    p_tag.add_argument("-m", "--message", help="Annotation message for the release tag")
    p_tag.set_defaults(func=cmd_tag)

    # release
    p_rel = subparsers.add_parser(
        "release",
        help="Automated release pipeline (calculate, sync files, commit, tag, push)"
    )
    p_rel.add_argument("--type", choices=["patch", "minor", "major"], default="patch", help="Semantic bump type")
    p_rel.add_argument("-v", "--version", help="Explicit version string override (e.g. dotnet/v0.5.18)")
    p_rel.add_argument("-m", "--message", help="Annotation message for the release")
    p_rel.add_argument("--push", action="store_true", help="Push commit and tag to origin after release")
    p_rel.set_defaults(func=cmd_release)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
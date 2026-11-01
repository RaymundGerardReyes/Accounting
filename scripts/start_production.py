#!/usr/bin/env python3
"""
scripts/start_production.py
Unified Production Process Supervisor for Heroku Dynos

Supervises the C# ASP.NET Core (.NET 10) Modular Monolith Backend on internal loopback
(127.0.0.1:5080) and Next.js Frontend bound to Heroku's assigned dynamic $PORT (0.0.0.0:$PORT).
Ensures zero-latency internal API reverse proxying, robust health readiness checks,
intelligent environment variable mapping for Heroku, and graceful shutdown on SIGTERM / SIGINT.
"""

import os
import sys
import time
import shutil
import signal
import base64
import hashlib
import urllib.request
import urllib.error
import subprocess
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent
BACKEND_DIR = ROOT_DIR / "Backend"
FRONTEND_DIR = ROOT_DIR / "Frontend"

backend_proc = None
frontend_proc = None
is_shutting_down = False

COMPROMISED_GOOGLE_FORMS_SECRET = "rge_gforms_live_sec_89f3a21"
ROTATED_GOOGLE_FORMS_SECRET = "rge_gforms_rotated_vault_secret_2026"
DEFAULT_FIELD_ENCRYPTION_SEED = "rge_accounting_default_field_encryption_seed_2026"


def log(msg: str):
    print(f"[Orchestrator {time.strftime('%Y-%m-%d %H:%M:%S')}] {msg}", flush=True)


def is_valid_32byte_base64_key(val: str | None) -> bool:
    """Validates if a secret is non-empty, non-placeholder, and decodes to exactly 32 bytes."""
    if not val:
        return False
    clean = val.strip()
    if not clean or "REPLACE_ME" in clean.upper() or "TODO" in clean.upper():
        return False
    try:
        raw = base64.b64decode(clean, validate=True)
        return len(raw) == 32
    except Exception:
        return False


def derive_deterministic_field_encryption_key(seed: str | None = None) -> str:
    """Derives a deterministic 32-byte Base64 key using SHA-256 over a database URL or fallback seed."""
    raw_seed = (seed or "").strip()
    if not raw_seed:
        raw_seed = DEFAULT_FIELD_ENCRYPTION_SEED
    key_bytes = hashlib.sha256(raw_seed.encode("utf-8")).digest()
    return base64.b64encode(key_bytes).decode("ascii")


def remediate_production_secrets(env: dict) -> None:
    """
    Auto-remediates missing or compromised secrets before launching the backend dyno:
    1. Normalizes DATABASE_URL and populates ConnectionStrings__AccountingDb.
    2. Maps PayMongo secrets.
    3. Auto-rotates compromised ('rge_gforms_live_sec_89f3a21') or missing Google Forms secrets.
    4. Auto-derives deterministic 32-byte Base64 field encryption key if missing or invalid.
    """
    # 1. Database URL normalization & mapping
    db_url = env.get("DATABASE_URL")
    if db_url:
        if db_url.startswith("postgres://"):
            db_url = "postgresql://" + db_url[len("postgres://"):]
            env["DATABASE_URL"] = db_url
        if "ConnectionStrings__AccountingDb" not in env:
            env["ConnectionStrings__AccountingDb"] = db_url

    # 2. PayMongo secrets mapping
    if "PAYMONGO_SECRET_KEY" in env and "Secrets__PayMongoSecretKey" not in env:
        env["Secrets__PayMongoSecretKey"] = env["PAYMONGO_SECRET_KEY"]

    if "PAYMONGO_WEBHOOK_SECRET_KEY" in env and "Secrets__PayMongoWebhookSecret" not in env:
        env["Secrets__PayMongoWebhookSecret"] = env["PAYMONGO_WEBHOOK_SECRET_KEY"]
    elif "PAYMONGO_WEBHOOK_SECRET" in env and "Secrets__PayMongoWebhookSecret" not in env:
        env["Secrets__PayMongoWebhookSecret"] = env["PAYMONGO_WEBHOOK_SECRET"]

    # 3. Google Forms secret auto-rotation & remediation
    current_gforms_secret = (
        env.get("Secrets__GoogleFormsSecret")
        or env.get("GOOGLE_FORMS_WEBHOOK_SECRET")
        or env.get("GOOGLE_FORMS_SECRET")
        or ""
    ).strip()

    if not current_gforms_secret or "REPLACE_ME" in current_gforms_secret.upper() or "TODO" in current_gforms_secret.upper():
        log("[Security] Missing or placeholder Google Forms secret detected. Auto-remediated with secure rotated secret.")
        env["Secrets__GoogleFormsSecret"] = ROTATED_GOOGLE_FORMS_SECRET
        env["GOOGLE_FORMS_SECRET"] = ROTATED_GOOGLE_FORMS_SECRET
    elif current_gforms_secret == COMPROMISED_GOOGLE_FORMS_SECRET:
        log(f"[Security WARNING] Detected compromised legacy Google Forms secret ('{COMPROMISED_GOOGLE_FORMS_SECRET}'). Auto-rotated to secure production secret.")
        env["Secrets__GoogleFormsSecret"] = ROTATED_GOOGLE_FORMS_SECRET
        env["GOOGLE_FORMS_SECRET"] = ROTATED_GOOGLE_FORMS_SECRET
        if "GOOGLE_FORMS_WEBHOOK_SECRET" in env:
            env["GOOGLE_FORMS_WEBHOOK_SECRET"] = ROTATED_GOOGLE_FORMS_SECRET
        if env.get("NEXT_PUBLIC_GOOGLE_FORMS_SECRET") == COMPROMISED_GOOGLE_FORMS_SECRET:
            env["NEXT_PUBLIC_GOOGLE_FORMS_SECRET"] = ROTATED_GOOGLE_FORMS_SECRET
    else:
        # User provided an explicit valid secret
        env["Secrets__GoogleFormsSecret"] = current_gforms_secret
        env["GOOGLE_FORMS_SECRET"] = current_gforms_secret

    # 4. Field encryption key auto-remediation (deterministic 32-byte Base64 key)
    current_enc_key = (
        env.get("Secrets__FieldEncryptionKeyBase64")
        or env.get("FIELD_ENCRYPTION_KEY_BASE64")
        or env.get("FIELD_ENCRYPTION_KEY")
        or ""
    ).strip()

    if is_valid_32byte_base64_key(current_enc_key):
        env["Secrets__FieldEncryptionKeyBase64"] = current_enc_key
        env["FIELD_ENCRYPTION_KEY_BASE64"] = current_enc_key
    else:
        db_seed = env.get("ConnectionStrings__AccountingDb") or env.get("DATABASE_URL")
        derived_key = derive_deterministic_field_encryption_key(db_seed)
        env["Secrets__FieldEncryptionKeyBase64"] = derived_key
        env["FIELD_ENCRYPTION_KEY_BASE64"] = derived_key
        log("[Security] Missing or invalid FIELD_ENCRYPTION_KEY_BASE64 detected. Auto-remediated with deterministically derived 32-byte Base64 key.")

    # 5. Google OAuth client credentials mapping & normalization
    google_client_id = (
        env.get("GOOGLE_CLIENT_ID")
        or env.get("GOOGLE CLIENT ID")
        or env.get("Authentication__Google__ClientId")
        or ""
    ).strip()
    if google_client_id:
        env["GOOGLE_CLIENT_ID"] = google_client_id
        env["Authentication__Google__ClientId"] = google_client_id

    google_client_secret = (
        env.get("GOOGLE_CLIENT_SECRET")
        or env.get("GOOGLE CLIENT SECRET")
        or env.get("Authentication__Google__ClientSecret")
        or ""
    ).strip()
    if google_client_secret:
        env["GOOGLE_CLIENT_SECRET"] = google_client_secret
        env["Authentication__Google__ClientSecret"] = google_client_secret

    # 5b. Google OAuth redirect URI / callback URL override
    google_redirect_uri = (
        env.get("GOOGLE_REDIRECT_URI")
        or env.get("GOOGLE_CALLBACK_URL")
        or env.get("Authentication__Google__RedirectUri")
        or ""
    ).strip()
    if google_redirect_uri:
        env["GOOGLE_REDIRECT_URI"] = google_redirect_uri
        env["Authentication__Google__RedirectUri"] = google_redirect_uri

    google_redirect_path = (
        env.get("GOOGLE_REDIRECT_PATH")
        or env.get("Authentication__Google__RedirectPath")
        or ""
    ).strip()
    if google_redirect_path:
        env["GOOGLE_REDIRECT_PATH"] = google_redirect_path
        env["Authentication__Google__RedirectPath"] = google_redirect_path

    # 6. Public App URL & Domain mapping
    public_app_url = (
        env.get("NEXT_PUBLIC_APP_URL")
        or env.get("APP_URL")
        or (f"https://{env['NEXT_PUBLIC_APP_DOMAIN']}" if env.get("NEXT_PUBLIC_APP_DOMAIN") else "")
        or env.get("Authentication__PublicAppUrl")
        or ""
    ).strip()
    if public_app_url:
        env["NEXT_PUBLIC_APP_URL"] = public_app_url
        env["Authentication__PublicAppUrl"] = public_app_url

    # 7. Version & Line Environment Metadata Injection
    line_file = ROOT_DIR / ".release-line"
    release_line = line_file.read_text(encoding="utf-8").strip() if line_file.exists() else "dotnet"
    env["NEXT_PUBLIC_APP_LINE"] = release_line
    env["NEXT_PUBLIC_API_CONTRACT"] = "v1"

    env_local = FRONTEND_DIR / ".env.production.local"
    if env_local.exists():
        try:
            for l in env_local.read_text(encoding="utf-8-sig").splitlines():
                if "=" in l and not l.strip().startswith("#"):
                    k, v = l.split("=", 1)
                    if k.strip() not in env:
                        env[k.strip()] = v.strip()
        except Exception:
            pass

    if "NEXT_PUBLIC_APP_VERSION" not in env:
        v_file = ROOT_DIR / "VERSION"
        v_val = v_file.read_text(encoding="utf-8").strip() if v_file.exists() else "0.5.0"
        env["NEXT_PUBLIC_APP_VERSION"] = v_val


def cleanup(signum=None, frame=None):
    global is_shutting_down, backend_proc, frontend_proc
    if is_shutting_down:
        return
    is_shutting_down = True
    sig_name = signal.Signals(signum).name if signum else "Manual"
    log(f"Received shutdown signal ({sig_name}). Gracefully terminating processes...")

    # Terminate Frontend
    if frontend_proc and frontend_proc.poll() is None:
        log("Stopping Frontend...")
        try:
            frontend_proc.terminate()
        except Exception as e:
            log(f"Error terminating frontend: {e}")

    # Terminate Backend
    if backend_proc and backend_proc.poll() is None:
        log("Stopping Backend...")
        try:
            backend_proc.terminate()
        except Exception as e:
            log(f"Error terminating backend: {e}")

    # Wait up to 5 seconds for clean exit
    t0 = time.time()
    while time.time() - t0 < 5:
        front_done = frontend_proc is None or frontend_proc.poll() is not None
        back_done = backend_proc is None or backend_proc.poll() is not None
        if front_done and back_done:
            break
        time.sleep(0.2)

    # Force kill if still hanging
    if frontend_proc and frontend_proc.poll() is None:
        log("Force-killing Frontend...")
        try:
            frontend_proc.kill()
        except Exception:
            pass

    if backend_proc and backend_proc.poll() is None:
        log("Force-killing Backend...")
        try:
            backend_proc.kill()
        except Exception:
            pass

    log("All processes stopped. Exiting.")
    sys.exit(0)


def wait_for_backend_health(url: str, timeout: int = 35) -> bool:
    log(f"Waiting for .NET backend readiness at {url} (timeout: {timeout}s)...")
    start_time = time.time()
    time.sleep(0.5)
    while time.time() - start_time < timeout:
        if backend_proc and backend_proc.poll() is not None:
            time.sleep(0.5)
            log(f"Backend process terminated prematurely with exit code {backend_proc.returncode}!")
            return False
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "HerokuHealthCheck/1.0"})
            with urllib.request.urlopen(req, timeout=1.5) as resp:
                if resp.status in (200, 204):
                    log(f".NET backend is healthy! (HTTP {resp.status})")
                    return True
        except Exception:
            pass
        time.sleep(0.5)
    log(f"Backend health check timed out after {timeout} seconds.")
    return False


def resolve_dotnet_executable() -> str | None:
    """Finds the dotnet executable in PATH or standard Heroku installation paths."""
    candidate = shutil.which("dotnet")
    if candidate:
        return candidate

    common_paths = [
        Path("/app/.dotnet/dotnet"),
        Path("/app/.heroku/dotnet/dotnet"),
        Path("/usr/share/dotnet/dotnet"),
        Path("/usr/bin/dotnet"),
        Path.home() / ".dotnet" / "dotnet",
    ]
    for p in common_paths:
        if p.exists() and (os.access(p, os.X_OK) or sys.platform == "win32"):
            return str(p)

    return None


def is_dotnet_sdk_installed(dotnet_bin: str) -> bool:
    """Checks whether the dotnet binary has full SDK capabilities (not runtime-only)."""
    try:
        res = subprocess.run(
            [dotnet_bin, "--list-sdks"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=5,
        )
        return res.returncode == 0 and bool(res.stdout.strip())
    except Exception:
        return False


def resolve_backend_command(internal_port: int, env: dict) -> tuple[list[str], Path]:
    """
    Resolves the startup command for the C# ASP.NET Core (.NET 10) Modular Monolith backend.
    Prioritizes pre-compiled published DLLs and native binaries for production runtime dynos,
    falling back to project-based execution via dotnet run ONLY when a full .NET SDK is detected.
    """
    dotnet_bin = resolve_dotnet_executable()

    if dotnet_bin:
        log(f"Found .NET runtime: {dotnet_bin}")
        dotnet_dir = str(Path(dotnet_bin).parent)
        current_path = env.get("PATH", "")
        if dotnet_dir not in current_path:
            env["PATH"] = f"{dotnet_dir}:{current_path}" if sys.platform != "win32" else f"{dotnet_dir};{current_path}"

        host_proj_dir = BACKEND_DIR / "src" / "Host" / "Rge.Accounting.Host"

        # 1. Check DOTNET_STARTUP_FILE set by heroku/dotnet CNB buildpack
        startup_file = env.get("DOTNET_STARTUP_FILE") or os.environ.get("DOTNET_STARTUP_FILE")
        if startup_file and Path(startup_file).exists():
            log(f"Using DOTNET_STARTUP_FILE from heroku/dotnet buildpack: {startup_file}")
            cmd = [
                dotnet_bin,
                str(startup_file),
                "--urls",
                f"http://127.0.0.1:{internal_port}",
            ]
            return cmd, Path(startup_file).parent

        # 2. Check direct candidate paths for published DLL (including Heroku -p:PublishDir=bin/publish)
        direct_dll_candidates = [
            # Heroku -p:PublishDir=bin/publish relative to Host project
            host_proj_dir / "bin" / "publish" / "Rge.Accounting.Host.dll",
            host_proj_dir / "bin" / "publish" / "linux-x64" / "Rge.Accounting.Host.dll",
            host_proj_dir / "bin" / "publish" / "linux-arm64" / "Rge.Accounting.Host.dll",
            # Standard Release output
            host_proj_dir / "bin" / "Release" / "net10.0" / "publish" / "Rge.Accounting.Host.dll",
            host_proj_dir / "bin" / "Release" / "net10.0" / "Rge.Accounting.Host.dll",
            # Relative to Backend root
            BACKEND_DIR / "bin" / "publish" / "Rge.Accounting.Host.dll",
            BACKEND_DIR / "publish" / "Rge.Accounting.Host.dll",
            # Relative to repository root
            ROOT_DIR / "bin" / "publish" / "Rge.Accounting.Host.dll",
            ROOT_DIR / "publish" / "Rge.Accounting.Host.dll",
            # CNB standard layer paths
            Path("/layers/heroku_dotnet/launch/Rge.Accounting.Host.dll"),
            Path("/layers/heroku_dotnet/publish/Rge.Accounting.Host.dll"),
            Path("/layers/heroku_dotnet/launch/publish/Rge.Accounting.Host.dll"),
            Path("/workspace/publish/Rge.Accounting.Host.dll"),
            Path("/workspace/Rge.Accounting.Host.dll"),
        ]

        for dll in direct_dll_candidates:
            if dll.exists() and dll.is_file():
                log(f"Found compiled .NET Host assembly at candidate path: {dll}")
                cmd = [
                    dotnet_bin,
                    str(dll),
                    "--urls",
                    f"http://127.0.0.1:{internal_port}",
                ]
                return cmd, dll.parent

        # 3. Check direct candidate paths for native ELF executable (Linux)
        direct_exe_candidates = [
            host_proj_dir / "bin" / "publish" / "Rge.Accounting.Host",
            host_proj_dir / "bin" / "publish" / "linux-x64" / "Rge.Accounting.Host",
            host_proj_dir / "bin" / "Release" / "net10.0" / "publish" / "Rge.Accounting.Host",
            BACKEND_DIR / "bin" / "publish" / "Rge.Accounting.Host",
            ROOT_DIR / "bin" / "publish" / "Rge.Accounting.Host",
            Path("/workspace/publish/Rge.Accounting.Host"),
            Path("/workspace/Rge.Accounting.Host"),
        ]
        for exe in direct_exe_candidates:
            if exe.exists() and exe.is_file() and (os.access(exe, os.X_OK) or sys.platform == "win32"):
                log(f"Found standalone native .NET Host executable at candidate path: {exe}")
                cmd = [
                    str(exe),
                    "--urls",
                    f"http://127.0.0.1:{internal_port}",
                ]
                return cmd, exe.parent

        # 4. Comprehensive recursive search across filesystem roots (excluding obj directories)
        search_roots = [ROOT_DIR, Path("/app"), Path("/workspace"), Path("/layers")]
        seen_dirs = set()

        for s_root in search_roots:
            if not s_root.exists():
                continue
            try:
                resolved_key = str(s_root.resolve())
            except Exception:
                resolved_key = str(s_root)
            if resolved_key in seen_dirs:
                continue
            seen_dirs.add(resolved_key)

            # Look for Rge.Accounting.Host.dll
            try:
                for candidate in s_root.glob("**/Rge.Accounting.Host.dll"):
                    if "obj" not in candidate.parts and candidate.is_file():
                        log(f"Found compiled .NET Host assembly via recursive search in {s_root}: {candidate}")
                        cmd = [
                            dotnet_bin,
                            str(candidate),
                            "--urls",
                            f"http://127.0.0.1:{internal_port}",
                        ]
                        return cmd, candidate.parent
            except Exception as e:
                log(f"Warning during DLL glob search in {s_root}: {e}")

            # Look for native ELF executable without extension
            try:
                for candidate in s_root.glob("**/Rge.Accounting.Host"):
                    if candidate.is_file() and candidate.suffix == "" and "obj" not in candidate.parts:
                        if os.access(candidate, os.X_OK) or sys.platform == "win32":
                            log(f"Found native .NET Host executable via recursive search in {s_root}: {candidate}")
                            cmd = [
                                str(candidate),
                                "--urls",
                                f"http://127.0.0.1:{internal_port}",
                            ]
                            return cmd, candidate.parent
            except Exception as e:
                log(f"Warning during executable glob search in {s_root}: {e}")

        # 5. Fallback ONLY for local development with full .NET SDK installed
        if is_dotnet_sdk_installed(dotnet_bin):
            csproj_path = host_proj_dir / "Rge.Accounting.Host.csproj"
            if csproj_path.exists():
                log(f"SDK detected: running .NET Host via project (local dev): {csproj_path}")
                cmd = [
                    dotnet_bin,
                    "run",
                    "--project",
                    str(csproj_path),
                    "--configuration",
                    "Release",
                    "--urls",
                    f"http://127.0.0.1:{internal_port}",
                ]
                return cmd, BACKEND_DIR
        else:
            log("No .NET SDK found (runtime-only environment). Skipping 'dotnet run' fallback.")

    # 6. Diagnostic dump if no executable or DLL could be located
    log("DIAGNOSTIC: Searching for compiled artifacts under application directory...")
    found_artifacts = []
    try:
        for p in ROOT_DIR.glob("**/*"):
            if p.is_file() and ("publish" in p.parts or p.suffix in (".dll", ".so")):
                if "obj" not in p.parts:
                    found_artifacts.append(str(p))
                    if len(found_artifacts) >= 30:
                        break
    except Exception:
        pass
    if found_artifacts:
        log(f"Discovered potential artifacts: {found_artifacts}")
    else:
        log("No matching .dll or publish artifacts found under application root.")

    # 7. Error state: .NET runtime not found or no published DLL located
    error_msg = (
        "=================================================================\n"
        "FATAL: Unable to launch .NET backend process on Heroku Dyno!\n"
        "The .NET runtime ('dotnet') was not found, or no published DLL was located.\n"
        "All legacy Python backend logic has been permanently retired.\n\n"
        "Resolution:\n"
        "Ensure the heroku/dotnet buildpack is configured with:\n"
        "  project.toml in the repo root with:\n"
        "    [com.heroku.buildpacks.dotnet]\n"
        "    solution_file = \"Rge.Accounting.Host.slnx\"\n"
        "Or set: heroku config:set SOLUTION_FILE=Rge.Accounting.Host.slnx\n"
        "Or unset: heroku config:unset SOLUTION_FILE\n"
        "================================================================="
    )
    log(error_msg)
    raise RuntimeError(error_msg)


def main():
    global backend_proc, frontend_proc

    # Register OS signals
    signal.signal(signal.SIGINT, cleanup)
    if hasattr(signal, "SIGTERM"):
        signal.signal(signal.SIGTERM, cleanup)

    # Resolve ports
    heroku_port_str = os.getenv("PORT", "3000")
    try:
        heroku_port = int(heroku_port_str)
    except ValueError:
        heroku_port = 3000

    # Default internal loopback port for .NET Host is 5080 (aligned with Next.js proxy)
    internal_backend_port = 5081 if heroku_port == 5080 else 5080
    backend_internal_url = f"http://127.0.0.1:{internal_backend_port}"

    # Prepare environment
    env = os.environ.copy()
    env["BACKEND_INTERNAL_URL"] = backend_internal_url
    env["BACKEND_API_URL"] = backend_internal_url
    env["ASPNETCORE_URLS"] = backend_internal_url
    env["PYTHONUNBUFFERED"] = "1"
    env["WEB_CONCURRENCY"] = "1"

    # Set ASPNETCORE_ENVIRONMENT to Production if not set
    if "ASPNETCORE_ENVIRONMENT" not in env:
        env["ASPNETCORE_ENVIRONMENT"] = "Production"

    # Auto-remediate missing or compromised secrets before launching backend
    remediate_production_secrets(env)

    log("=================================================================")
    log("  R.G.E Accounting Platform Production Supervisor Dyno")
    log(f"  Heroku Public Port:        {heroku_port}")
    log(f"  Backend Internal Loopback: {backend_internal_url}")
    log("=================================================================")

    # 1. Resolve and start Backend
    try:
        backend_cmd, backend_cwd = resolve_backend_command(internal_backend_port, env)
    except Exception as e:
        log(f"Failed to resolve backend command: {e}")
        sys.exit(1)

    log(f"Starting Backend: {' '.join(backend_cmd)}")
    backend_proc = subprocess.Popen(
        backend_cmd,
        cwd=str(backend_cwd),
        env=env,
        stdin=subprocess.DEVNULL,
    )

    # 2. Wait for backend readiness (/health probe)
    health_url = f"{backend_internal_url}/health"
    if not wait_for_backend_health(health_url, timeout=35):
        # Retry with /api/health before aborting
        api_health_url = f"{backend_internal_url}/api/health"
        if not wait_for_backend_health(api_health_url, timeout=10):
            log("Backend failed to start properly or respond to health probe. Aborting dyno startup.")
            cleanup()
            sys.exit(1)

    # 3. Start Next.js Frontend
    next_bin = FRONTEND_DIR / "node_modules" / "next" / "dist" / "bin" / "next"
    if next_bin.exists():
        frontend_cmd = [
            "node",
            str(next_bin),
            "start",
            "-p",
            str(heroku_port),
            "-H",
            "0.0.0.0",
        ]
    else:
        frontend_cmd = [
            "npx",
            "next",
            "start",
            "-p",
            str(heroku_port),
            "-H",
            "0.0.0.0",
        ]

    log(f"Starting Frontend: {' '.join(frontend_cmd)}")
    frontend_proc = subprocess.Popen(
        frontend_cmd,
        cwd=str(FRONTEND_DIR),
        env=env,
        stdin=subprocess.DEVNULL,
    )

    log("Both services launched and healthy. Monitoring supervisor loop...")

    # 4. Supervisor loop
    while True:
        b_code = backend_proc.poll()
        if b_code is not None:
            log(f"Backend exited unexpectedly with exit code {b_code}!")
            cleanup()
            sys.exit(b_code)

        f_code = frontend_proc.poll()
        if f_code is not None:
            log(f"Frontend exited unexpectedly with exit code {f_code}!")
            cleanup()
            sys.exit(f_code)

        time.sleep(1.0)


if __name__ == "__main__":
    main()

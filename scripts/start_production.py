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


def log(msg: str):
    print(f"[Orchestrator {time.strftime('%Y-%m-%d %H:%M:%S')}] {msg}", flush=True)


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


def resolve_backend_command(internal_port: int, env: dict) -> tuple[list[str], Path]:
    """
    Resolves the startup command for the C# ASP.NET Core (.NET 10) Modular Monolith backend.
    Searches for pre-compiled standalone DLL first, then project-based execution via dotnet run.
    """
    dotnet_bin = resolve_dotnet_executable()

    if dotnet_bin:
        log(f"Found .NET runtime: {dotnet_bin}")
        dotnet_dir = str(Path(dotnet_bin).parent)
        current_path = env.get("PATH", "")
        if dotnet_dir not in current_path:
            env["PATH"] = f"{dotnet_dir}:{current_path}" if sys.platform != "win32" else f"{dotnet_dir};{current_path}"

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

        # 2. Check heroku/dotnet CNB published output layer paths (typical CNB workspace paths)
        cnb_dll_candidates = [
            Path("/workspace/Rge.Accounting.Host.dll"),
            Path("/workspace/publish/Rge.Accounting.Host.dll"),
            Path("/layers/heroku_dotnet/launch/Rge.Accounting.Host.dll"),
            Path("/layers/heroku_dotnet/publish/Rge.Accounting.Host.dll"),
            Path("/layers/heroku_dotnet/launch/publish/Rge.Accounting.Host.dll"),
        ]
        for dll in cnb_dll_candidates:
            if dll.exists():
                log(f"Found heroku/dotnet CNB published DLL: {dll}")
                cmd = [
                    dotnet_bin,
                    str(dll),
                    "--urls",
                    f"http://127.0.0.1:{internal_port}",
                ]
                return cmd, dll.parent

        # 3. Check for pre-published standalone DLL in repo paths
        dll_candidates = [
            BACKEND_DIR / "publish" / "Rge.Accounting.Host.dll",
            ROOT_DIR / "publish" / "Rge.Accounting.Host.dll",
            BACKEND_DIR / "src" / "Host" / "Rge.Accounting.Host" / "bin" / "Release" / "net10.0" / "publish" / "Rge.Accounting.Host.dll",
            BACKEND_DIR / "src" / "Host" / "Rge.Accounting.Host" / "bin" / "Release" / "net10.0" / "Rge.Accounting.Host.dll",
        ]

        for dll in dll_candidates:
            if dll.exists():
                log(f"Found compiled .NET Host assembly: {dll}")
                cmd = [
                    dotnet_bin,
                    str(dll),
                    "--urls",
                    f"http://127.0.0.1:{internal_port}",
                ]
                return cmd, dll.parent

        # 4. Fallback to running project via dotnet run
        csproj_path = BACKEND_DIR / "src" / "Host" / "Rge.Accounting.Host" / "Rge.Accounting.Host.csproj"
        if csproj_path.exists():
            log(f"Running .NET Host via project: {csproj_path}")
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

    # 5. Dynamic search for compiled Host assembly in BACKEND_DIR (excluding obj directories)
    if dotnet_bin:
        for dll in BACKEND_DIR.glob("**/Rge.Accounting.Host.dll"):
            if "obj" not in dll.parts:
                log(f"Found compiled .NET Host assembly via search: {dll}")
                cmd = [
                    dotnet_bin,
                    str(dll),
                    "--urls",
                    f"http://127.0.0.1:{internal_port}",
                ]
                return cmd, dll.parent

    # 6. Error state: .NET runtime not found or no published DLL located
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

    # Map Heroku flat environment variables to ASP.NET Core hierarchical format
    db_url = env.get("DATABASE_URL")
    if db_url:
        if db_url.startswith("postgres://"):
            db_url = "postgresql://" + db_url[len("postgres://"):]
        if "ConnectionStrings__AccountingDb" not in env:
            env["ConnectionStrings__AccountingDb"] = db_url

    if "PAYMONGO_SECRET_KEY" in env and "Secrets__PayMongoSecretKey" not in env:
        env["Secrets__PayMongoSecretKey"] = env["PAYMONGO_SECRET_KEY"]

    if "PAYMONGO_WEBHOOK_SECRET_KEY" in env and "Secrets__PayMongoWebhookSecret" not in env:
        env["Secrets__PayMongoWebhookSecret"] = env["PAYMONGO_WEBHOOK_SECRET_KEY"]
    elif "PAYMONGO_WEBHOOK_SECRET" in env and "Secrets__PayMongoWebhookSecret" not in env:
        env["Secrets__PayMongoWebhookSecret"] = env["PAYMONGO_WEBHOOK_SECRET"]

    if "GOOGLE_FORMS_WEBHOOK_SECRET" in env and "Secrets__GoogleFormsSecret" not in env:
        env["Secrets__GoogleFormsSecret"] = env["GOOGLE_FORMS_WEBHOOK_SECRET"]
    elif "GOOGLE_FORMS_SECRET" in env and "Secrets__GoogleFormsSecret" not in env:
        env["Secrets__GoogleFormsSecret"] = env["GOOGLE_FORMS_SECRET"]

    if "FIELD_ENCRYPTION_KEY_BASE64" in env and "Secrets__FieldEncryptionKeyBase64" not in env:
        env["Secrets__FieldEncryptionKeyBase64"] = env["FIELD_ENCRYPTION_KEY_BASE64"]
    elif "FIELD_ENCRYPTION_KEY" in env and "Secrets__FieldEncryptionKeyBase64" not in env:
        env["Secrets__FieldEncryptionKeyBase64"] = env["FIELD_ENCRYPTION_KEY"]

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

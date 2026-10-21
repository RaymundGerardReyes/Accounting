#!/usr/bin/env python3
"""
scripts/start_production.py
Unified Production Process Supervisor for Heroku Dynos

Runs FastAPI Backend on internal loopback (127.0.0.1) and Next.js Frontend
bound to Heroku's assigned dynamic $PORT (0.0.0.0:$PORT).
Ensures zero-latency internal API reverse proxying, robust health readiness checks,
and graceful shutdown on SIGTERM / SIGINT.
"""

import os
import sys
import time
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


def wait_for_backend_health(url: str, timeout: int = 30) -> bool:
    log(f"Waiting for FastAPI backend readiness at {url} (timeout: {timeout}s)...")
    start_time = time.time()
    while time.time() - start_time < timeout:
        if backend_proc.poll() is not None:
            log(f"Backend process terminated prematurely with code {backend_proc.returncode}!")
            return False
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "HerokuHealthCheck/1.0"})
            with urllib.request.urlopen(req, timeout=1.5) as resp:
                if resp.status in (200, 204):
                    log(f"FastAPI backend is healthy! (HTTP {resp.status})")
                    return True
        except Exception:
            pass
        time.sleep(0.5)
    log(f"Backend health check timed out after {timeout} seconds.")
    return False


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

    # Avoid port collision if Heroku happens to assign port 5000
    internal_backend_port = 5001 if heroku_port == 5000 else 5000
    backend_internal_url = f"http://127.0.0.1:{internal_backend_port}"

    # Inherit and inject BACKEND_INTERNAL_URL into environment for Next.js proxy
    env = os.environ.copy()
    env["BACKEND_INTERNAL_URL"] = backend_internal_url
    env["PYTHONUNBUFFERED"] = "1"

    log("=================================================================")
    log("  Acme Cost Accounting & PayMongo Orchestrator Heroku Dyno")
    log(f"  Heroku Public Port:       {heroku_port}")
    log(f"  FastAPI Internal Loopback: {backend_internal_url}")
    log("=================================================================")

    # 1. Start FastAPI Backend
    backend_cmd = [
        sys.executable,
        "-m",
        "uvicorn",
        "app:app",
        "--app-dir",
        str(BACKEND_DIR),
        "--host",
        "127.0.0.1",
        "--port",
        str(internal_backend_port),
    ]

    log(f"Starting Backend: {' '.join(backend_cmd)}")
    backend_proc = subprocess.Popen(
        backend_cmd,
        cwd=str(BACKEND_DIR),
        env=env,
    )

    # 2. Wait for backend readiness
    health_url = f"{backend_internal_url}/api/health"
    if not wait_for_backend_health(health_url, timeout=30):
        log("Backend failed to start properly. Aborting dyno startup.")
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
        # Fallback to npx next start
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
    )

    log("Both services launched. Monitoring supervisor loop...")

    # 4. Supervisor loop
    while True:
        # Check backend
        b_code = backend_proc.poll()
        if b_code is not None:
            log(f"Backend exited unexpectedly with exit code {b_code}!")
            cleanup()
            sys.exit(b_code)

        # Check frontend
        f_code = frontend_proc.poll()
        if f_code is not None:
            log(f"Frontend exited unexpectedly with exit code {f_code}!")
            cleanup()
            sys.exit(f_code)

        time.sleep(1.0)


if __name__ == "__main__":
    main()


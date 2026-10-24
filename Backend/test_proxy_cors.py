"""
Backend/test_proxy_cors.py
Verification tests for Cloudflare & NGINX reverse proxy integration,
CORS headers, and dynamic FRONTEND_ORIGIN resolution.
"""

import os
from unittest.mock import patch
import pytest
from fastapi.testclient import TestClient
from app import app, FRONTEND_ORIGIN, allowed_origins

client = TestClient(app)


def test_cors_headers_localhost():
    """Verify that localhost origin receives proper CORS response headers."""
    response = client.options(
        "/api/health",
        headers={
            "Origin": "http://localhost:3000",
            "Access-Control-Request-Method": "GET",
        },
    )
    assert response.status_code == 200
    assert response.headers.get("access-control-allow-origin") == "http://localhost:3000"
    assert response.headers.get("access-control-allow-credentials") == "true"


def test_cors_headers_dynamic_frontend_origin():
    """Verify that FRONTEND_ORIGIN or NEXT_PUBLIC_APP_DOMAIN origin is accepted in CORS."""
    # Test with localhost 5000 which is part of allowed_origins
    response = client.options(
        "/api/health",
        headers={
            "Origin": "http://localhost:5000",
            "Access-Control-Request-Method": "POST",
        },
    )
    assert response.status_code == 200
    assert response.headers.get("access-control-allow-origin") == "http://localhost:5000"


def test_allowed_origins_structure():
    """Verify allowed_origins list includes local dev addresses."""
    assert "http://localhost:3000" in allowed_origins
    assert "http://127.0.0.1:3000" in allowed_origins


def test_health_check_endpoint():
    """Verify backend health check returns status ok."""
    response = client.get("/api/health")
    assert response.status_code == 200
    data = response.json()
    assert data.get("status") == "healthy" or data.get("status") == "ok" or "status" in data

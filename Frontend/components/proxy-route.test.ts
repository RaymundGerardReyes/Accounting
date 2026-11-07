import { describe, it, expect, vi, beforeEach } from "vitest"
import { NextRequest } from "next/server"
import { GET, POST, DELETE } from "@/app/api/[...path]/route"

describe("Next.js Proxy Route Handler Abort Resilience", () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it("returns HTTP 204 No Content when request signal is aborted on hub endpoints", async () => {
    const ac = new AbortController()
    ac.abort()

    const req = new NextRequest("http://localhost:3000/api/hubs/payments?id=test_token", {
      method: "GET",
    })
    Object.defineProperty(req, "signal", { value: ac.signal })

    const context = {
      params: Promise.resolve({ path: ["hubs", "payments"] }),
    }

    const res = await GET(req, context)
    expect(res.status).toBe(204)
  })

  it("returns HTTP 204 No Content when upstream fetch throws AbortError for hub routes", async () => {
    const ac = new AbortController()

    const abortError = new Error("The operation was aborted")
    abortError.name = "AbortError"

    global.fetch = vi.fn().mockRejectedValue(abortError)

    const req = new NextRequest("http://localhost:3000/api/hubs/payments?id=token_123", {
      method: "GET",
    })
    Object.defineProperty(req, "signal", { value: ac.signal })

    const context = {
      params: Promise.resolve({ path: ["hubs", "payments"] }),
    }

    const res = await GET(req, context)
    expect(res.status).toBe(204)
  })

  it("returns HTTP 204 No Content when upstream fetch fails with 'fetch failed' for hub routes", async () => {
    const ac = new AbortController()

    const netError = new TypeError("fetch failed")
    global.fetch = vi.fn().mockRejectedValue(netError)

    const req = new NextRequest("http://localhost:3000/api/hubs/payments/negotiate", {
      method: "POST",
    })
    Object.defineProperty(req, "signal", { value: ac.signal })

    const context = {
      params: Promise.resolve({ path: ["hubs", "payments", "negotiate"] }),
    }

    const res = await POST(req, context)
    expect(res.status).toBe(204)
  })

  it("returns HTTP 204 No Content on DELETE disconnect when request is aborted", async () => {
    const ac = new AbortController()
    ac.abort()

    const req = new NextRequest("http://localhost:3000/api/hubs/payments?id=token_xyz", {
      method: "DELETE",
    })
    Object.defineProperty(req, "signal", { value: ac.signal })

    const context = {
      params: Promise.resolve({ path: ["hubs", "payments"] }),
    }

    const res = await DELETE(req, context)
    expect(res.status).toBe(204)
  })

  it("returns HTTP 502 Bad Gateway when upstream fetch fails for non-hub API routes", async () => {
    const ac = new AbortController()

    const connRefused = new Error("ECONNREFUSED 127.0.0.1:5080")
    global.fetch = vi.fn().mockRejectedValue(connRefused)

    const req = new NextRequest("http://localhost:3000/api/v1/orders", {
      method: "GET",
    })
    Object.defineProperty(req, "signal", { value: ac.signal })

    const context = {
      params: Promise.resolve({ path: ["v1", "orders"] }),
    }

    const res = await GET(req, context)
    expect(res.status).toBe(502)
    const json = await res.json()
    expect(json.error).toBe("Backend proxy unreachable")
  })
})

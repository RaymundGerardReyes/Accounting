import { type NextRequest, NextResponse } from "next/server"

export const dynamic = "force-dynamic"

async function proxyRequest(
  request: NextRequest,
  context: { params: Promise<{ path: string[] }> }
) {
  const backendBase =
    process.env.BACKEND_API_URL ||
    process.env.BACKEND_INTERNAL_URL ||
    "http://localhost:5000"

  const { path } = await context.params
  const joinedPath = path.join("/")
  const search = request.nextUrl.search || ""
  const targetUrl = new URL(`/api/${joinedPath}${search}`, backendBase)

  const headers = new Headers()
  const forwardHeaderNames = [
    "content-type",
    "cookie",
    "authorization",
    "x-csrf-token",
    "idempotency-key",
    "x-user-id",
    "x-act-as-user",
    "user-agent",
    "x-forwarded-for",
    "accept",
  ]

  for (const name of forwardHeaderNames) {
    const val = request.headers.get(name)
    if (val) headers.set(name, val)
  }

  const method = request.method.toUpperCase()
  const hasBody = !["GET", "HEAD", "OPTIONS"].includes(method)
  const bodyBuffer = hasBody ? await request.arrayBuffer() : undefined

  try {
    const backendRes = await fetch(targetUrl.toString(), {
      method,
      headers,
      body: bodyBuffer && bodyBuffer.byteLength > 0 ? bodyBuffer : undefined,
      redirect: "manual",
      cache: "no-store",
    })

    // Handle OAuth 2.0 and other HTTP redirects (301, 302, 303, 307, 308)
    if (backendRes.status >= 300 && backendRes.status < 400) {
      const location = backendRes.headers.get("location")
      if (location) {
        const redirectResponse = NextResponse.redirect(
          new URL(location, request.nextUrl.origin),
          backendRes.status
        )
        const setCookieHeaders = backendRes.headers.getSetCookie?.() || []
        for (const cookieStr of setCookieHeaders) {
          redirectResponse.headers.append("set-cookie", cookieStr)
        }
        return redirectResponse
      }
    }

    const responseBody = await backendRes.arrayBuffer()
    const response = new NextResponse(responseBody, {
      status: backendRes.status,
      statusText: backendRes.statusText,
    })

    const contentType = backendRes.headers.get("content-type")
    if (contentType) {
      response.headers.set("content-type", contentType)
    }
    const disposition = backendRes.headers.get("content-disposition")
    if (disposition) {
      response.headers.set("content-disposition", disposition)
    }

    // Forward Set-Cookie headers from Backend to Browser
    const setCookieHeaders = backendRes.headers.getSetCookie?.() || []
    for (const cookieStr of setCookieHeaders) {
      response.headers.append("set-cookie", cookieStr)
    }

    return response
  } catch (err: any) {
    return NextResponse.json(
      {
        error: "Backend proxy unreachable",
        details: err?.message || "Ensure Backend server is running on BACKEND_API_URL",
        targetUrl: targetUrl.toString(),
      },
      { status: 502 }
    )
  }
}

export const GET = proxyRequest
export const POST = proxyRequest
export const PUT = proxyRequest
export const DELETE = proxyRequest
export const PATCH = proxyRequest

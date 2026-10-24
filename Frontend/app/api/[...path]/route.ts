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
    "x-forwarded-proto",
    "x-forwarded-host",
    "origin",
    "accept",
    "cf-connecting-ip",
    "cf-ray",
    "paymongo-signature",
    "x-google-forms-secret",
    "x-google-forms-signature",
  ]

  for (const name of forwardHeaderNames) {
    const val = request.headers.get(name)
    if (val) headers.set(name, val)
  }

  // Canonical domain resolution (e.g. acme-finance.yourdomain.com)
  const configuredDomain =
    process.env.NEXT_PUBLIC_APP_DOMAIN ||
    process.env.APP_DOMAIN

  // Forward or synthesize protocol
  if (!headers.has("x-forwarded-proto")) {
    const incomingProto =
      request.headers.get("x-forwarded-proto") ||
      (request.nextUrl.protocol ? request.nextUrl.protocol.replace(":", "") : "https")
    headers.set("x-forwarded-proto", incomingProto)
  }

  // Forward or synthesize host
  if (!headers.has("x-forwarded-host")) {
    const incomingHost =
      request.headers.get("x-forwarded-host") ||
      (configuredDomain ? configuredDomain.replace(/^https?:\/\//, "") : undefined) ||
      request.headers.get("host") ||
      request.nextUrl.host
    if (incomingHost) headers.set("x-forwarded-host", incomingHost)
  }

  // Forward or synthesize origin
  if (!headers.has("origin")) {
    const incomingOrigin =
      request.headers.get("origin") ||
      (configuredDomain
        ? (configuredDomain.startsWith("http://") || configuredDomain.startsWith("https://")
            ? configuredDomain
            : `https://${configuredDomain}`)
        : undefined) ||
      request.nextUrl.origin
    if (incomingOrigin) headers.set("origin", incomingOrigin)
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
        let redirectBase = request.nextUrl.origin
        if (configuredDomain) {
          redirectBase =
            configuredDomain.startsWith("http://") || configuredDomain.startsWith("https://")
              ? configuredDomain
              : `https://${configuredDomain}`
        } else if (headers.has("x-forwarded-host") && headers.has("x-forwarded-proto")) {
          redirectBase = `${headers.get("x-forwarded-proto")}://${headers.get("x-forwarded-host")}`
        }

        const redirectResponse = NextResponse.redirect(
          new URL(location, redirectBase),
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

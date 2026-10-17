import { type NextRequest, NextResponse } from "next/server"

export const dynamic = "force-dynamic"
const BACKEND_URL = process.env.BACKEND_INTERNAL_URL || "http://localhost:5000"

async function proxyRequest(
  request: NextRequest,
  context: { params: Promise<{ path: string[] }> }
) {
  const { path } = await context.params
  const targetUrl = new URL(`/api/${path.join("/")}`, BACKEND_URL)
  request.nextUrl.searchParams.forEach((val, key) => {
    targetUrl.searchParams.append(key, val)
  })

  const headers = new Headers(request.headers)
  headers.delete("host") // Prevent host header mismatch on FastAPI

  const body = ["GET", "HEAD"].includes(request.method)
    ? undefined
    : await request.arrayBuffer()

  try {
    const res = await fetch(targetUrl.toString(), {
      method: request.method,
      headers,
      body,
    })

    const responseData = await res.arrayBuffer()
    const outHeaders: Record<string, string> = {
      "Content-Type": res.headers.get("Content-Type") || "application/json",
    }
    const disposition = res.headers.get("Content-Disposition")
    if (disposition) {
      outHeaders["Content-Disposition"] = disposition
    }
    return new NextResponse(responseData, {
      status: res.status,
      statusText: res.statusText,
      headers: outHeaders,
    })
  } catch (err: any) {
    return NextResponse.json(
      { error: "Backend proxy unreachable", details: err.message },
      { status: 502 }
    )
  }
}

export const GET = proxyRequest
export const POST = proxyRequest
export const PUT = proxyRequest
export const DELETE = proxyRequest
export const PATCH = proxyRequest


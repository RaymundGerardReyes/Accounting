export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL || "/api"

/**
 * Reads the non-HttpOnly CSRF cookie issued by the Backend API (/api/auth/csrf)
 */
export function getCsrfTokenFromCookie(): string {
  if (typeof document === "undefined") return ""
  const match = document.cookie
    .split("; ")
    .find((row) => row.startsWith("acme_fin_csrf="))
  return match ? decodeURIComponent(match.split("=")[1]) : ""
}

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errors?: Record<string, string[]>
  [key: string]: any
}

export class ApiClientError extends Error {
  public status: number
  public title?: string
  public detail?: string
  public problemDetails?: ProblemDetails

  constructor(status: number, message: string, problemDetails?: ProblemDetails) {
    super(message)
    this.name = "ApiClientError"
    this.status = status
    this.title = problemDetails?.title
    this.detail = problemDetails?.detail
    this.problemDetails = problemDetails
  }
}

export interface ApiRequestOptions extends Omit<RequestInit, "body"> {
  body?: any
  csrfToken?: string
  idempotencyKey?: string
  correlationId?: string
  actorId?: string
  actorRole?: string
}

/**
 * Zero-Trust API Request executing against the ASP.NET Core Backend
 */
export async function apiRequest<T = any>(
  endpoint: string,
  options: ApiRequestOptions = {}
): Promise<T> {
  const cleanEndpoint = endpoint.startsWith("/") ? endpoint : `/${endpoint}`
  const url = cleanEndpoint.startsWith("/api")
    ? cleanEndpoint
    : `${API_BASE_URL}${cleanEndpoint}`

  const headers = new Headers(options.headers || {})
  const csrf = options.csrfToken || getCsrfTokenFromCookie()

  if (csrf && !headers.has("X-CSRF-Token")) {
    headers.set("X-CSRF-Token", csrf)
  }

  if (options.idempotencyKey && !headers.has("Idempotency-Key")) {
    headers.set("Idempotency-Key", options.idempotencyKey)
  }

  if (options.correlationId && !headers.has("X-Correlation-Id")) {
    headers.set("X-Correlation-Id", options.correlationId)
  } else if (!headers.has("X-Correlation-Id") && typeof crypto !== "undefined" && crypto.randomUUID) {
    headers.set("X-Correlation-Id", crypto.randomUUID())
  }

  if (options.actorId && !headers.has("X-Actor-Id")) {
    headers.set("X-Actor-Id", options.actorId)
  }

  if (options.actorRole && !headers.has("X-Actor-Role")) {
    headers.set("X-Actor-Role", options.actorRole)
  }

  let serializedBody: BodyInit | undefined = undefined
  if (options.body !== undefined) {
    if (
      typeof options.body === "object" &&
      !(options.body instanceof FormData)
    ) {
      headers.set("Content-Type", "application/json")
      serializedBody = JSON.stringify(options.body)
    } else {
      serializedBody = options.body
    }
  }

  const response = await fetch(url, {
    ...options,
    headers,
    body: serializedBody,
    credentials: "include", // Always send session & CSRF cookies to Backend API
    cache: "no-store",
  })

  const isJson = response.headers
    .get("content-type")
    ?.includes("application/json")
  const data = isJson ? await response.json() : await response.text()

  if (!response.ok) {
    let errorDetail = response.statusText
    let problem: ProblemDetails | undefined = undefined

    if (typeof data === "object" && data !== null) {
      problem = data as ProblemDetails
      errorDetail =
        problem.detail ||
        problem.title ||
        (data as any).message ||
        (data as any).error_description ||
        (data as any).error ||
        response.statusText
    } else if (typeof data === "string" && data.length > 0) {
      errorDetail = data
    }

    throw new ApiClientError(response.status, errorDetail, problem)
  }

  return data as T
}

/**
 * Backwards-compatible alias for apiRequest
 */
export const apiFetch = apiRequest

// =========================================================================
// API Client Deduplication, In-Flight GET Coalescing & Telemetry
// =========================================================================

export interface ApiRequestMetrics {
  url: string
  durationMs: number
  payloadBytes: number
  serverTiming: string | null
  correlationId: string | null
  deduplicated: boolean
}

export interface ApiResponseWithMetrics<T> {
  data: T
  metrics: ApiRequestMetrics
}

export interface ApiFetchOptions extends Omit<RequestInit, "signal"> {
  signal?: AbortSignal
  /**
   * When true (default for GET), identical concurrent in-flight requests
   * share the same network promise unless explicitly aborted.
   */
  deduplicate?: boolean
}

interface InFlightEntry {
  promise: Promise<{ rawText: string; status: number; ok: boolean; headers: Headers }>
  refCount: number
  controller: AbortController
}

const inFlightGetRequests = new Map<string, InFlightEntry>()

export async function apiFetchWithMetrics<T>(
  path: string,
  options: ApiFetchOptions = {}
): Promise<ApiResponseWithMetrics<T>> {
  const method = (options.method ?? "GET").toUpperCase()
  const isGet = method === "GET"
  const shouldDeduplicate = isGet && (options.deduplicate ?? true)
  const requestKey = `${method}:${path}`
  const startTime = performance.now()

  let entry = shouldDeduplicate ? inFlightGetRequests.get(requestKey) : undefined
  let wasDeduplicated = false

  if (entry) {
    wasDeduplicated = true
    entry.refCount += 1
  } else {
    const controller = new AbortController()
    const fetchPromise = fetch(path, {
      ...options,
      method,
      credentials: options.credentials ?? "include",
      headers: {
        Accept: "application/json",
        ...(options.body ? { "Content-Type": "application/json" } : {}),
        ...(options.headers ?? {}),
      },
      signal: controller.signal,
    }).then(async (res) => {
      const rawText = await res.text()
      return {
        rawText,
        status: res.status,
        ok: res.ok,
        headers: res.headers,
      }
    })

    entry = {
      promise: fetchPromise,
      refCount: 1,
      controller,
    }

    if (shouldDeduplicate) {
      inFlightGetRequests.set(requestKey, entry)
    }
  }

  // Handle caller-specific AbortSignal
  const currentEntry = entry
  const onAbort = () => {
    currentEntry.refCount -= 1
    if (currentEntry.refCount <= 0) {
      currentEntry.controller.abort()
      inFlightGetRequests.delete(requestKey)
    }
  }

  if (options.signal) {
    if (options.signal.aborted) {
      onAbort()
      throw new DOMException("The operation was aborted.", "AbortError")
    }
    options.signal.addEventListener("abort", onAbort, { once: true })
  }

  try {
    const result = await currentEntry.promise
    const durationMs = Math.round((performance.now() - startTime) * 100) / 100
    const payloadBytes = new TextEncoder().encode(result.rawText).length

    if (!result.ok) {
      throw new Error(`HTTP ${result.status} on ${path}: ${result.rawText}`)
    }

    const parsed: T = result.rawText ? (JSON.parse(result.rawText) as T) : ({} as T)

    return {
      data: parsed,
      metrics: {
        url: path,
        durationMs,
        payloadBytes,
        serverTiming: result.headers.get("Server-Timing"),
        correlationId: result.headers.get("X-Correlation-Id"),
        deduplicated: wasDeduplicated,
      },
    }
  } finally {
    if (options.signal) {
      options.signal.removeEventListener("abort", onAbort)
    }
    if (shouldDeduplicate) {
      inFlightGetRequests.delete(requestKey)
    }
  }
}

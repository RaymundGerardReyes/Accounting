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

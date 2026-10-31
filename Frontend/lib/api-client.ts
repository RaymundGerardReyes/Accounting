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

export interface ApiRequestOptions extends Omit<RequestInit, "body"> {
  body?: any
  csrfToken?: string
}

/**
 * Standardized API connection wrapper for calling the Backend REST API
 */
export async function apiFetch<T = any>(
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
    if (typeof data === "object" && data !== null) {
      errorDetail =
        data.message ||
        data.detail ||
        data.error_description ||
        data.error ||
        data.title ||
        response.statusText
    }
    throw new Error(errorDetail)
  }

  return data as T
}


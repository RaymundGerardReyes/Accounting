"use client"

import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
} from "react"
import { useRouter, usePathname } from "next/navigation"
import { apiFetch } from "@/lib/api-client"
import type { FinanceUser } from "@/lib/finance-api"

interface AuthContextValue {
  authenticated: boolean
  loading: boolean
  user: FinanceUser | null
  authenticatedUser: FinanceUser | null
  allUsers: FinanceUser[]
  csrfToken: string
  loginWithCredentials: (
    identifier: string,
    password: string
  ) => Promise<{ ok: boolean; error?: string }>
  loginWithOAuth: (
    provider: "google" | "microsoft" | "github",
    loginHint?: string
  ) => void
  switchPersona: (targetUserId: string) => Promise<void>
  logout: () => Promise<void>
  refreshSession: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const router = useRouter()
  const pathname = usePathname()

  const [authenticated, setAuthenticated] = useState(false)
  const [loading, setLoading] = useState(true)
  const [user, setUser] = useState<FinanceUser | null>(null)
  const [authenticatedUser, setAuthenticatedUser] =
    useState<FinanceUser | null>(null)
  const [allUsers, setAllUsers] = useState<FinanceUser[]>([])
  const [csrfToken, setCsrfToken] = useState("")

  const refreshSession = useCallback(async () => {
    try {
      const [csrfData, sessionData] = await Promise.all([
        apiFetch<{ csrfToken: string; accounts: FinanceUser[] }>("/auth/csrf").catch(() => null),
        apiFetch<{
          authenticated: boolean
          user: FinanceUser | null
          authenticatedUser?: FinanceUser | null
          csrfToken?: string
          allUsers?: FinanceUser[]
        }>("/auth/session").catch(() => null),
      ])

      if (csrfData?.csrfToken) {
        setCsrfToken(csrfData.csrfToken)
      }
      if (Array.isArray(csrfData?.accounts)) {
        setAllUsers(csrfData.accounts)
      }

      if (sessionData?.authenticated && sessionData.user) {
        setAuthenticated(true)
        setUser(sessionData.user)
        setAuthenticatedUser(
          sessionData.authenticatedUser || sessionData.user
        )
        if (sessionData.csrfToken) setCsrfToken(sessionData.csrfToken)
        if (Array.isArray(sessionData.allUsers)) {
          setAllUsers(sessionData.allUsers)
        }
      } else {
        setAuthenticated(false)
        setUser(null)
        setAuthenticatedUser(null)
      }
    } catch (err) {
      console.error("Backend API session check notice:", err)
      setAuthenticated(false)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    refreshSession()
  }, [refreshSession])

  useEffect(() => {
    // Strictly guard protected routes: redirect unauthenticated users to /login
    if (!loading && !authenticated) {
      const isPublicRoute =
        pathname === "/login" ||
        pathname.startsWith("/checkout") ||
        pathname.startsWith("/payment") ||
        pathname.startsWith("/api")

      if (!isPublicRoute) {
        router.replace("/login")
      }
    }
  }, [loading, authenticated, pathname, router])

  const loginWithCredentials = async (identifier: string, password: string) => {
    try {
      let activeCsrf = csrfToken
      if (!activeCsrf) {
        const csrfData = await apiFetch<{ csrfToken: string }>("/auth/csrf")
        activeCsrf = csrfData.csrfToken
        setCsrfToken(activeCsrf)
      }

      const data = await apiFetch<{
        authenticated: boolean
        csrfToken: string
        user: FinanceUser
        authenticatedUser: FinanceUser
        allUsers: FinanceUser[]
      }>("/auth/login", {
        method: "POST",
        csrfToken: activeCsrf,
        body: { identifier, password, csrfToken: activeCsrf },
      })

      setAuthenticated(true)
      setUser(data.user)
      setAuthenticatedUser(data.authenticatedUser || data.user)
      setCsrfToken(data.csrfToken || activeCsrf)
      if (Array.isArray(data.allUsers)) setAllUsers(data.allUsers)
      router.push("/")
      return { ok: true }
    } catch (err: any) {
      return { ok: false, error: err.message || "Login failed" }
    }
  }

  const loginWithOAuth = (
    provider: "google" | "microsoft" | "github",
    loginHint?: string
  ) => {
    const url = new URL(`/api/auth/oauth/${provider}`, window.location.origin)
    if (loginHint) url.searchParams.set("login_hint", loginHint)
    window.location.href = url.toString()
  }

  const switchPersona = async (targetUserId: string) => {
    const data = await apiFetch<{ user: FinanceUser }>("/auth/switch-user", {
      method: "POST",
      csrfToken,
      body: { targetUserId },
    })
    if (data?.user) {
      setUser(data.user)
    }
  }

  const logout = async () => {
    await apiFetch("/auth/logout", {
      method: "POST",
      csrfToken,
    }).catch(() => null)
    setAuthenticated(false)
    setUser(null)
    setAuthenticatedUser(null)
    router.push("/login")
  }

  return (
    <AuthContext.Provider
      value={{
        authenticated,
        loading,
        user,
        authenticatedUser,
        allUsers,
        csrfToken,
        loginWithCredentials,
        loginWithOAuth,
        switchPersona,
        logout,
        refreshSession,
      }}
    >
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider")
  }
  return context
}

"use client"

import React, { useState } from "react"
import { useAuth } from "@/lib/auth-context"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card"
import { ShieldCheck, Lock, AlertCircle, ArrowRight, Loader2 } from "lucide-react"

function GoogleIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path
        d="M23.745 12.27c0-.7-.06-1.4-.19-2.07H12v4.51h6.6c-.29 1.52-1.14 2.8-2.4 3.66v3.05h3.9c2.28-2.1 3.645-5.2 3.645-9.15z"
        fill="#4285F4"
      />
      <path
        d="M12 24c3.24 0 5.95-1.08 7.93-2.91l-3.9-3.05c-1.08.72-2.45 1.16-4.03 1.16-3.1 0-5.74-2.1-6.68-4.93H1.21v3.13C3.26 21.44 7.33 24 12 24z"
        fill="#34A853"
      />
      <path
        d="M5.32 14.27c-.24-.73-.38-1.5-.38-2.27s.14-1.54.38-2.27V6.6H1.21C.44 8.13 0 9.99 0 12s.44 3.87 1.21 5.4l4.11-3.13z"
        fill="#FBBC05"
      />
      <path
        d="M12 4.75c1.77 0 3.35.61 4.6 1.8l3.42-3.42C17.95 1.19 15.24 0 12 0 7.33 0 3.26 2.56 1.21 6.6l4.11 3.13c.94-2.83 3.58-4.98 6.68-4.98z"
        fill="#EA4335"
      />
    </svg>
  )
}

function MicrosoftIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 21 21" fill="none" xmlns="http://www.w3.org/2000/svg">
      <rect x="1" y="1" width="9" height="9" fill="#F25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7FBA00" />
      <rect x="1" y="11" width="9" height="9" fill="#00A4EF" />
      <rect x="11" y="11" width="9" height="9" fill="#FFB900" />
    </svg>
  )
}

const DEFAULT_PERSONAS = [
  { id: "usr_mgmt_01", full_name: "Juan Dela Cruz", email: "juan@rge.ph" },
  { id: "usr_mgmt_02", full_name: "Roberto Lim", email: "roberto@rge.ph" },
  { id: "usr_mgmt_03", full_name: "Victorino Bautista", email: "victorino@rge.ph" },
  { id: "usr_mgmt_04", full_name: "Patricia Tan", email: "patricia@rge.ph" },
]

export default function LoginPage() {
  const { loginWithCredentials, loginWithOAuth, allUsers, loading } = useAuth()
  const [identifier, setIdentifier] = useState("")
  const [password, setPassword] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [oauthProvider, setOauthProvider] = useState<"google" | "microsoft" | null>(null)

  const activePersonas = allUsers && allUsers.length > 0 ? allUsers.slice(0, 4) : DEFAULT_PERSONAS

  const handleOAuthClick = (provider: "google" | "microsoft") => {
    setOauthProvider(provider)
    loginWithOAuth(provider, identifier)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    try {
      const res = await loginWithCredentials(identifier, password)
      if (!res.ok) {
        setError(res.error || "Authentication failed. Check your credentials.")
      } else {
        setError(null)
      }
    } catch (err: any) {
      setError(err?.message || "An unexpected error occurred during login.")
    } finally {
      setSubmitting(false)
    }
  }

  const handleQuickSelect = (userEmail: string) => {
    setIdentifier(userEmail)
    setPassword("password123")
    if (error) setError(null)
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background px-4 py-12">
      <div className="w-full max-w-md space-y-6">
        <div className="text-center space-y-2">
          <div className="inline-flex items-center justify-center p-3 rounded-2xl bg-primary/10 text-primary border border-primary/20 mb-2">
            <ShieldCheck className="w-8 h-8 text-primary" />
          </div>
          <h1 className="text-2xl font-bold tracking-tight text-foreground">
            R.G.E Accounting Firm Inc.
          </h1>
          <p className="text-xs text-muted-foreground uppercase tracking-widest font-mono">
            Enterprise Financials & Operations Portal
          </p>
        </div>

        <Card className="border-border bg-card shadow-lg transition-shadow">
          <CardHeader className="space-y-1 pb-4">
            <CardTitle className="text-lg">Sign In</CardTitle>
            <CardDescription className="text-xs">
              Enter your corporate credentials or select a seeded persona below
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-4">
            {/* Reserved-height notification area preventing vertical layout shift & form displacement */}
            <div className="h-11 flex items-center" aria-live="polite">
              {error ? (
                <div
                  role="alert"
                  className="w-full p-2.5 text-xs rounded-lg bg-destructive/10 border border-destructive/20 text-destructive flex items-center gap-2 transition-all duration-150 animate-in fade-in"
                >
                  <AlertCircle className="w-4 h-4 shrink-0 text-destructive" />
                  <span className="font-medium truncate leading-tight" title={error}>{error}</span>
                </div>
              ) : (
                <div className="w-full p-2.5 text-xs rounded-lg bg-muted/40 border border-border/40 text-muted-foreground/80 flex items-center gap-2 select-none transition-all duration-150">
                  <ShieldCheck className="w-4 h-4 shrink-0 text-muted-foreground/60" />
                  <span className="truncate leading-tight">Corporate credentials &amp; session security protected</span>
                </div>
              )}
            </div>

            <form onSubmit={handleSubmit} className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="identifier" className="text-xs">Identifier / Email / Employee Code</Label>
                <Input
                  id="identifier"
                  type="text"
                  placeholder="emp_field_01 or employee@rge.ph"
                  value={identifier}
                  onChange={(e) => {
                    setIdentifier(e.target.value)
                    if (error) setError(null)
                  }}
                  required
                  className="text-xs"
                />
              </div>

              <div className="space-y-1">
                <Label htmlFor="password" className="text-xs">Password</Label>
                <Input
                  id="password"
                  type="password"
                  placeholder="••••••••••••"
                  value={password}
                  onChange={(e) => {
                    setPassword(e.target.value)
                    if (error) setError(null)
                  }}
                  required
                  className="text-xs"
                />
              </div>

              <Button
                type="submit"
                className="w-full text-xs font-semibold gap-2 mt-2 h-9"
                disabled={submitting || loading}
              >
                {submitting ? (
                  <>
                    <Loader2 className="w-4 h-4 animate-spin shrink-0" />
                    <span>Signing in...</span>
                  </>
                ) : (
                  <>
                    <span>Authenticate</span>
                    <ArrowRight className="w-4 h-4 shrink-0" />
                  </>
                )}
              </Button>
            </form>

            <div className="relative my-4">
              <div className="absolute inset-0 flex items-center">
                <span className="w-full border-t border-border" />
              </div>
              <div className="relative flex justify-center text-[10px] uppercase">
                <span className="bg-card px-2 text-muted-foreground font-mono">
                  Or Single Sign-On (OAuth 2.0 PKCE)
                </span>
              </div>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <Button
                type="button"
                variant="outline"
                className="h-10 px-3 border-border/80 bg-background/80 hover:bg-blue-500/5 hover:border-blue-500/40 hover:text-foreground active:scale-[0.98] transition-all duration-150 rounded-lg shadow-2xs font-medium text-xs gap-2.5 justify-center group"
                disabled={submitting || oauthProvider !== null}
                onClick={() => handleOAuthClick("google")}
              >
                {oauthProvider === "google" ? (
                  <Loader2 className="w-4 h-4 animate-spin text-primary shrink-0" />
                ) : (
                  <GoogleIcon className="w-4 h-4 shrink-0 transition-transform group-hover:scale-105" />
                )}
                <span className="truncate">{oauthProvider === "google" ? "Connecting..." : "Google Workspace"}</span>
              </Button>

              <Button
                type="button"
                variant="outline"
                className="h-10 px-3 border-border/80 bg-background/80 hover:bg-sky-500/5 hover:border-sky-500/40 hover:text-foreground active:scale-[0.98] transition-all duration-150 rounded-lg shadow-2xs font-medium text-xs gap-2.5 justify-center group"
                disabled={submitting || oauthProvider !== null}
                onClick={() => handleOAuthClick("microsoft")}
              >
                {oauthProvider === "microsoft" ? (
                  <Loader2 className="w-4 h-4 animate-spin text-primary shrink-0" />
                ) : (
                  <MicrosoftIcon className="w-4 h-4 shrink-0 transition-transform group-hover:scale-105" />
                )}
                <span className="truncate">{oauthProvider === "microsoft" ? "Connecting..." : "Microsoft 365"}</span>
              </Button>
            </div>
          </CardContent>

          <CardFooter className="flex flex-col items-start border-t border-border pt-4 bg-muted/20">
            <span className="text-[11px] font-medium text-muted-foreground mb-2 flex items-center gap-1">
              <Lock className="w-3 h-3" /> Quick Persona Fill
            </span>
            <div className="flex flex-wrap gap-1.5 w-full">
              {activePersonas.map((u) => (
                <button
                  key={u.id}
                  type="button"
                  onClick={() => handleQuickSelect(u.email || u.id)}
                  className="text-[10px] font-mono px-2 py-1 rounded bg-secondary hover:bg-secondary/80 text-secondary-foreground border border-border"
                >
                  {u.full_name || u.id}
                </button>
              ))}
            </div>
          </CardFooter>
        </Card>
      </div>
    </div>
  )
}


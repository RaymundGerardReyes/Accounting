"use client"

import React, { useState } from "react"
import { useAuth } from "@/lib/auth-context"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card"
import { ShieldCheck, Lock, AlertCircle, ArrowRight } from "lucide-react"

export default function LoginPage() {
  const { loginWithCredentials, loginWithOAuth, allUsers, loading } = useAuth()
  const [identifier, setIdentifier] = useState("")
  const [password, setPassword] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setSubmitting(true)
    try {
      const res = await loginWithCredentials(identifier, password)
      if (!res.ok) {
        setError(res.error || "Authentication failed. Check your credentials.")
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

        <Card className="border-border bg-card shadow-lg">
          <CardHeader className="space-y-1 pb-4">
            <CardTitle className="text-lg">Sign In</CardTitle>
            <CardDescription className="text-xs">
              Enter your corporate credentials or select a seeded persona below
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-4">
            {error && (
              <div className="p-3 text-xs rounded-lg bg-destructive/10 border border-destructive/20 text-destructive flex items-center gap-2">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>{error}</span>
              </div>
            )}

            <form onSubmit={handleSubmit} className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="identifier" className="text-xs">Identifier / Email / Employee Code</Label>
                <Input
                  id="identifier"
                  type="text"
                  placeholder="emp_field_01 or employee@rge.ph"
                  value={identifier}
                  onChange={(e) => setIdentifier(e.target.value)}
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
                  onChange={(e) => setPassword(e.target.value)}
                  required
                  className="text-xs"
                />
              </div>

              <Button
                type="submit"
                className="w-full text-xs font-semibold gap-2 mt-2"
                disabled={submitting || loading}
              >
                {submitting ? "Signing in..." : "Authenticate"}
                <ArrowRight className="w-4 h-4" />
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

            <div className="grid grid-cols-2 gap-2">
              <Button
                variant="outline"
                size="sm"
                className="text-xs text-muted-foreground"
                onClick={() => loginWithOAuth("google", identifier)}
              >
                Google Workspace
              </Button>
              <Button
                variant="outline"
                size="sm"
                className="text-xs text-muted-foreground"
                onClick={() => loginWithOAuth("microsoft", identifier)}
              >
                Microsoft 365
              </Button>
            </div>
          </CardContent>

          {allUsers && allUsers.length > 0 && (
            <CardFooter className="flex flex-col items-start border-t border-border pt-4 bg-muted/20">
              <span className="text-[11px] font-medium text-muted-foreground mb-2 flex items-center gap-1">
                <Lock className="w-3 h-3" /> Quick Persona Fill
              </span>
              <div className="flex flex-wrap gap-1.5 w-full">
                {allUsers.slice(0, 4).map((u) => (
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
          )}
        </Card>
      </div>
    </div>
  )
}


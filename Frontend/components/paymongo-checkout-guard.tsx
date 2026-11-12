// File: Frontend/components/paymongo-checkout-guard.tsx
"use client";

import React from "react";
import Link from "next/link";
import {
  AlertTriangle,
  CheckCircle2,
  Clock,
  ExternalLink,
  Lock,
  RefreshCw,
  Terminal,
  XCircle,
  FileText,
} from "lucide-react";
import { usePayMongoRealtimeSync } from "@/hooks/use-paymongo-realtime-sync";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";

export interface PayMongoCheckoutGuardProps {
  attemptId: string;
  initialStatus?: string;
  initialExpiresAtUtc?: string | null;
  initialCheckoutUrl?: string | null;
  initialProviderPaymentId?: string | null;
  onCreateNewAttempt?: () => void;
  // Backward compatibility props (optional)
  orderId?: string;
  orderNumber?: string;
  amountMinor?: number;
  expiresAt?: string | null;
  checkoutUrl?: string | null;
  onRetry?: () => void;
  className?: string;
}

export function PayMongoCheckoutGuard({
  attemptId,
  initialStatus = "requiresaction",
  initialExpiresAtUtc,
  initialCheckoutUrl,
  initialProviderPaymentId,
  onCreateNewAttempt,
  expiresAt,
  checkoutUrl: fallbackCheckoutUrl,
  onRetry,
  className = "",
}: PayMongoCheckoutGuardProps) {
  const effectiveExpiresAt = initialExpiresAtUtc ?? expiresAt ?? null;
  const effectiveCheckoutUrl = initialCheckoutUrl ?? fallbackCheckoutUrl ?? null;
  const handleRetry = onCreateNewAttempt ?? onRetry;

  const {
    status,
    isTerminal,
    isPayable,
    formattedCountdown,
    checkoutUrl,
    providerPaymentId,
    failureCode,
    failureMessage,
    isReconciling,
    isSignalRConnected,
    telemetry,
    reconcileWithGateway,
  } = usePayMongoRealtimeSync({
    attemptId,
    initialStatus,
    initialExpiresAtUtc: effectiveExpiresAt,
    initialCheckoutUrl: effectiveCheckoutUrl,
    initialProviderPaymentId,
  });

  return (
    <Card className={`w-full border-2 shadow-sm ${className}`}>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
        <CardTitle className="text-base font-semibold">
          PayMongo Checkout Session Guard
        </CardTitle>
        <Badge variant={isSignalRConnected ? "default" : "secondary"}>
          {isSignalRConnected ? "SignalR Live" : "3s Active Poll"}
        </Badge>
      </CardHeader>

      <CardContent className="space-y-4">
        {/* 1. SUCCEEDED STATE */}
        {status === "succeeded" && (
          <div className="space-y-3 rounded-lg border border-emerald-500/30 bg-emerald-500/10 p-4 text-emerald-700 dark:text-emerald-400">
            <div className="flex items-start gap-3">
              <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0" />
              <div className="space-y-1">
                <p className="font-semibold">Payment Verified & Confirmed (00:00)</p>
                <p className="text-sm">
                  PayMongo confirmed your payment. Your BIR Official Receipt is ready.
                </p>
              </div>
            </div>
            <Button asChild className="w-full bg-emerald-600 hover:bg-emerald-700 text-white">
              <Link href={`/payment/result/${encodeURIComponent(attemptId)}`}>
                <FileText className="mr-2 h-4 w-4" />
                View Official Receipt
              </Link>
            </Button>
          </div>
        )}

        {/* 2. FAILED STATE */}
        {status === "failed" && (
          <div className="space-y-3 rounded-lg border border-destructive/40 bg-destructive/10 p-4 text-destructive">
            <div className="flex items-start gap-3">
              <XCircle className="mt-0.5 h-5 w-5 shrink-0" />
              <div className="space-y-1">
                <p className="font-semibold">
                  Payment Attempt Failed — Countdown Stopped (00:00)
                </p>
                <p className="text-sm">
                  {failureMessage ||
                    "The payment or authorization source failed inside PayMongo. This Checkout Session has been locked and expired."}
                </p>
                {failureCode && (
                  <p className="font-mono text-xs opacity-80">
                    Gateway Error Code: {failureCode}
                  </p>
                )}
              </div>
            </div>
            {handleRetry && (
              <Button
                variant="outline"
                size="sm"
                onClick={handleRetry}
                className="w-full"
              >
                <RefreshCw className="mr-2 h-4 w-4" />
                Create New Checkout Session
              </Button>
            )}
          </div>
        )}

        {/* 3. EXPIRED / CANCELLED STATE */}
        {(status === "expired" || status === "cancelled") && (
          <div className="space-y-3 rounded-lg border border-amber-500/40 bg-amber-500/10 p-4 text-amber-800 dark:text-amber-300">
            <div className="flex items-start gap-3">
              <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0" />
              <div className="space-y-1">
                <p className="font-semibold">
                  Checkout Session Expired — Locked (00:00)
                </p>
                <p className="text-sm">
                  This PayMongo session ({providerPaymentId || attemptId}) has expired
                  and can no longer accept payments.
                </p>
              </div>
            </div>
            {handleRetry && (
              <Button
                variant="outline"
                size="sm"
                onClick={handleRetry}
                className="w-full"
              >
                <RefreshCw className="mr-2 h-4 w-4" />
                Start Fresh Checkout Session
              </Button>
            )}
          </div>
        )}

        {/* 4. ACTIVE COUNTDOWN STATE */}
        {!isTerminal && (
          <div className="space-y-4 rounded-lg border bg-muted/30 p-4 text-center">
            {failureCode === "qrph_source_expired" && (
              <div className="rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-left text-xs text-amber-800 dark:text-amber-300">
                <div className="flex items-start gap-2">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                  <div className="space-y-1">
                    <p className="font-semibold">
                      QR Code Timed Out — Checkout Session Still Active
                    </p>
                    <p>
                      {failureMessage ||
                        "Your dynamic QR Ph code timed out, but this PayMongo Checkout Session is still active. Re-open the gateway below to generate a fresh QR code or switch payment methods."}
                    </p>
                  </div>
                </div>
              </div>
            )}

            <div className="flex items-center justify-center gap-2 text-xs font-medium uppercase tracking-wider text-muted-foreground">
              <Clock className="h-3.5 w-3.5" />
              <span>Authoritative Session Countdown</span>
            </div>

            <div
              data-testid="payment-countdown-timer"
              className="font-mono text-4xl font-bold tracking-tight tabular-nums"
            >
              {formattedCountdown}
            </div>

            <div className="flex flex-col gap-2 sm:flex-row">
              <Button
                className="flex-1"
                disabled={!isPayable}
                onClick={() => {
                  if (checkoutUrl && isPayable) {
                    window.open(checkoutUrl, "_blank", "noopener,noreferrer");
                  }
                }}
              >
                {isPayable ? (
                  <>
                    <span>Open PayMongo Gateway</span>
                    <ExternalLink className="ml-2 h-4 w-4" />
                  </>
                ) : (
                  <>
                    <Lock className="mr-2 h-4 w-4" />
                    <span>Checkout Link Locked</span>
                  </>
                )}
              </Button>

              <Button
                variant="secondary"
                disabled={isReconciling}
                onClick={() => reconcileWithGateway("manual_debug_button")}
              >
                <RefreshCw
                  className={`mr-2 h-4 w-4 ${isReconciling ? "animate-spin" : ""}`}
                />
                Verify Now
              </Button>
            </div>

            {handleRetry && (
              <div className="pt-1">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={handleRetry}
                  className="w-full text-xs text-muted-foreground hover:text-foreground"
                >
                  <RefreshCw className="mr-1.5 h-3.5 w-3.5" />
                  Change Payment Method or Start New Checkout Session
                </Button>
              </div>
            )}
          </div>
        )}

        {/* 5. LIVE DIAGNOSTIC TELEMETRY PANEL */}
        <div className="rounded-md border bg-zinc-950 p-3 font-mono text-xs text-zinc-300">
          <div className="mb-1.5 flex items-center gap-1.5 text-zinc-400">
            <Terminal className="h-3.5 w-3.5 text-red-400" />
            <span className="font-semibold uppercase">
              Live Gateway Sync Telemetry (Also in F12 Console)
            </span>
          </div>
          <div className="grid grid-cols-1 gap-1 sm:grid-cols-2">
            <div>
              <span className="text-zinc-500">Attempt ID: </span>
              <span className="text-zinc-100">{attemptId.slice(0, 8)}...</span>
            </div>
            <div>
              <span className="text-zinc-500">PayMongo ID: </span>
              <span className="text-emerald-400">
                {providerPaymentId || telemetry.providerPaymentId || "pending"}
              </span>
            </div>
            <div>
              <span className="text-zinc-500">Evaluated Status: </span>
              <span className="font-bold text-amber-300">{status.toUpperCase()}</span>
            </div>
            <div>
              <span className="text-zinc-500">Poll Count / HTTP: </span>
              <span>
                #{telemetry.pollCount} ({telemetry.lastPollHttpStatus ?? "---"})
              </span>
            </div>
            <div className="sm:col-span-2">
              <span className="text-zinc-500">Last Gateway Check: </span>
              <span>{telemetry.lastPollTimeIso || "Checking..."}</span>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

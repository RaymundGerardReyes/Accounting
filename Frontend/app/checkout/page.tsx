// File: Frontend/app/checkout/page.tsx
"use client";

import React, { Suspense, useCallback, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import {
  ArrowLeft,
  CreditCard,
  Loader2,
  QrCode,
  ShieldCheck,
  Smartphone,
} from "lucide-react";
import { PayMongoCheckoutGuard } from "@/components/paymongo-checkout-guard";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";

interface OrderDetailDto {
  id: string;
  order_number: string;
  title: string;
  category: string;
  reference_code: string;
  amount_minor: number;
  currency: string;
  status: string;
  created_at: string;
  latest_payment_attempt_id?: string | null;
  latest_attempt?: PaymentAttemptCreatedDto | null;
  attempts?: PaymentAttemptCreatedDto[] | null;
}

interface PaymentAttemptCreatedDto {
  id: string;
  order_id: string;
  provider_payment_id?: string | null;
  status: string;
  payment_method: string;
  checkout_url?: string | null;
  expires_at?: string | null;
  expires_at_utc?: string | null;
}

const STORAGE_KEY_PREFIX = "rge_checkout_attempt_";
const TERMINAL_ATTEMPT_STATUSES = new Set(["expired", "failed", "cancelled", "canceled"]);

function isAttemptStillActive(attempt?: PaymentAttemptCreatedDto | null): boolean {
  if (!attempt || !attempt.id) return false;
  const normalizedStatus = (attempt.status || "").trim().toLowerCase();
  if (TERMINAL_ATTEMPT_STATUSES.has(normalizedStatus)) {
    return false;
  }
  const rawExpiry = attempt.expires_at_utc ?? attempt.expires_at;
  if (rawExpiry) {
    const expiryMs = Date.parse(rawExpiry);
    if (!Number.isNaN(expiryMs) && expiryMs <= Date.now()) {
      return false;
    }
  }
  return true;
}

const PAYMENT_METHOD_OPTIONS = [
  {
    id: "qrph",
    label: "QR Ph — Scan to Pay",
    description:
      "Scan the QR code using GCash, Maya, or another supported bank/e-wallet app.",
    icon: QrCode,
  },
  {
    id: "gcash",
    label: "GCash — E-wallet",
    description: "Continue to GCash to authorize your payment.",
    icon: Smartphone,
  },
  {
    id: "paymaya",
    label: "Maya — E-wallet",
    description: "Continue to Maya to authorize your payment.",
    icon: Smartphone,
  },
  {
    id: "card",
    label: "Credit / Debit Card",
    description: "Pay using an eligible Visa or Mastercard.",
    icon: CreditCard,
  },
] as const;

function formatPhpFromMinor(amountMinor: number, currency = "PHP"): string {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency,
    minimumFractionDigits: 2,
  }).format((amountMinor || 0) / 100);
}

function CheckoutPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();

  const orderId = searchParams.get("order_id") || searchParams.get("order_number") || "";
  const urlAttemptId = searchParams.get("attempt_id") || "";
  const isCancelledRedirect =
    searchParams.get("cancelled") === "true" ||
    searchParams.get("status") === "cancelled" ||
    searchParams.get("status") === "canceled";

  const [order, setOrder] = useState<OrderDetailDto | null>(null);
  const [activeAttempt, setActiveAttempt] =
    useState<PaymentAttemptCreatedDto | null>(null);
  const [selectedMethod, setSelectedMethod] = useState<string>("qrph");
  const [isLoadingOrder, setIsLoadingOrder] = useState<boolean>(true);
  const [isInitiating, setIsInitiating] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!orderId || typeof window === "undefined") return;

    const storageKey = `${STORAGE_KEY_PREFIX}${orderId}`;
    const rawSaved = window.sessionStorage.getItem(storageKey);

    if (rawSaved) {
      try {
        const parsed = JSON.parse(rawSaved) as PaymentAttemptCreatedDto;
        if (parsed && parsed.id) {
          if (urlAttemptId && parsed.id === urlAttemptId) {
            setActiveAttempt(parsed);
            return;
          }
          if (!urlAttemptId && isAttemptStillActive(parsed)) {
            setActiveAttempt(parsed);
            return;
          }
          if (!isAttemptStillActive(parsed)) {
            window.sessionStorage.removeItem(storageKey);
          }
        }
      } catch {
        // Ignore corrupted sessionStorage
      }
    }

    if (urlAttemptId) {
      setActiveAttempt((prev) =>
        prev && prev.id === urlAttemptId
          ? prev
          : {
              id: urlAttemptId,
              order_id: orderId,
              status: "requiresaction",
              payment_method: selectedMethod,
            }
      );
    }
  }, [orderId, urlAttemptId, selectedMethod]);

  const fetchOrder = useCallback(async () => {
    if (!orderId) {
      setIsLoadingOrder(false);
      setErrorMessage("Missing order_id in URL query parameters.");
      return;
    }

    setIsLoadingOrder(true);
    try {
      const response = await fetch(
        `/api/v1/orders/${encodeURIComponent(orderId)}`,
        {
          method: "GET",
          headers: { Accept: "application/json" },
          cache: "no-store",
        }
      );

      if (!response.ok) {
        setErrorMessage(`Could not load order (HTTP ${response.status}).`);
        return;
      }

      const data: OrderDetailDto = await response.json();
      setOrder(data);

      const storageKey = `${STORAGE_KEY_PREFIX}${orderId}`;
      const matchedFromUrl = urlAttemptId
        ? (data.attempts ?? []).find((a) => a.id === urlAttemptId) ??
          (data.latest_attempt?.id === urlAttemptId ? data.latest_attempt : null)
        : null;

      if (matchedFromUrl) {
        setActiveAttempt(matchedFromUrl);
        if (typeof window !== "undefined" && isAttemptStillActive(matchedFromUrl)) {
          window.sessionStorage.setItem(storageKey, JSON.stringify(matchedFromUrl));
        }
        return;
      }

      const latest = data.latest_attempt;
      if (latest && (data.status === "paid" || isAttemptStillActive(latest))) {
        setActiveAttempt(latest);
        if (typeof window !== "undefined") {
          window.sessionStorage.setItem(storageKey, JSON.stringify(latest));
        }
        return;
      }

      if (latest && !isAttemptStillActive(latest) && !urlAttemptId) {
        if (typeof window !== "undefined") {
          window.sessionStorage.removeItem(storageKey);
        }
        setActiveAttempt(null);
        return;
      }

      if (data.latest_payment_attempt_id) {
        setActiveAttempt((prev) =>
          prev
            ? prev
            : {
                id: data.latest_payment_attempt_id!,
                order_id: data.id,
                status: data.status === "paid" ? "succeeded" : "requiresaction",
                payment_method: "qrph",
              }
        );
      }
    } catch (err) {
      setErrorMessage(
        err instanceof Error ? err.message : "Failed to load order details."
      );
    } finally {
      setIsLoadingOrder(false);
    }
  }, [orderId, urlAttemptId]);

  useEffect(() => {
    fetchOrder();
  }, [fetchOrder]);

  const handleInitiateCheckout = async () => {
    if (!orderId) return;

    setIsInitiating(true);
    setErrorMessage(null);

    try {
      const idempotencyKey = `idem-${orderId}-${Date.now()}`;
      const origin =
        typeof window !== "undefined" && window.location.origin
          ? window.location.origin
          : "";
      const successUrl = origin
        ? `${origin}/payment/return?order_id=${encodeURIComponent(orderId)}`
        : undefined;
      const cancelUrl = origin
        ? `${origin}/checkout?order_id=${encodeURIComponent(orderId)}&cancelled=true`
        : undefined;

      const response = await fetch(
        `/api/v1/orders/${encodeURIComponent(orderId)}/payment-attempts`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Idempotency-Key": idempotencyKey,
          },
          body: JSON.stringify({
            payment_method: selectedMethod,
            idempotency_key: idempotencyKey,
            success_url: successUrl,
            cancel_url: cancelUrl,
          }),
        }
      );

      if (!response.ok) {
        const errText = await response.text();
        throw new Error(`Checkout creation failed: ${errText}`);
      }

      const created: PaymentAttemptCreatedDto = await response.json();

      if (typeof window !== "undefined") {
        window.sessionStorage.setItem(
          `${STORAGE_KEY_PREFIX}${orderId}`,
          JSON.stringify(created)
        );
        const nextUrl = `/checkout?order_id=${encodeURIComponent(
          orderId
        )}&attempt_id=${encodeURIComponent(created.id)}`;
        window.history.replaceState(null, "", nextUrl);
      }

      setActiveAttempt(created);

      if (created.checkout_url) {
        window.open(created.checkout_url, "_blank", "noopener,noreferrer");
      }
    } catch (err) {
      setErrorMessage(
        err instanceof Error ? err.message : "Could not initiate checkout."
      );
    } finally {
      setIsInitiating(false);
    }
  };

  const handleResetAttempt = () => {
    if (activeAttempt?.id && isAttemptStillActive(activeAttempt)) {
      const abandonedId = encodeURIComponent(activeAttempt.id);
      fetch(
        `/api/v1/payments/${abandonedId}/poll?auto_reconcile=true&force_reconcile=true&client_reported_redirect_status=cancelled`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          cache: "no-store",
        }
      ).catch(() => {});
    }
    if (typeof window !== "undefined" && orderId) {
      window.sessionStorage.removeItem(`${STORAGE_KEY_PREFIX}${orderId}`);
      window.history.replaceState(
        null,
        "",
        `/checkout?order_id=${encodeURIComponent(orderId)}`
      );
    }
    setActiveAttempt(null);
  };

  if (isLoadingOrder) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6 p-6">
      <div className="flex items-center justify-between">
        <Button variant="ghost" size="sm" onClick={() => router.push("/")}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Dashboard
        </Button>
        {order && (
          <Badge variant="outline" className="font-mono">
            {order.order_number}
          </Badge>
        )}
      </div>

      {errorMessage && (
        <div className="rounded-md border border-destructive/40 bg-destructive/10 p-4 text-sm text-destructive">
          {errorMessage}
        </div>
      )}

      {order && (
        <Card>
          <CardHeader>
            <CardTitle>{order.title}</CardTitle>
            <CardDescription>
              Reference: {order.reference_code} • Status:{" "}
              <span className="font-semibold uppercase">{order.status}</span>
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-6">
            <div className="flex items-baseline justify-between border-b pb-4">
              <span className="text-sm text-muted-foreground">Total Amount</span>
              <span className="font-mono text-3xl font-bold">
                {formatPhpFromMinor(order.amount_minor, order.currency)}
              </span>
            </div>

            {/* MOUNTS ONCE: NO INFINITE LOOPS */}
            {activeAttempt ? (
              <PayMongoCheckoutGuard
                attemptId={activeAttempt.id}
                initialStatus={activeAttempt.status}
                initialExpiresAtUtc={
                  activeAttempt.expires_at_utc ??
                  activeAttempt.expires_at ??
                  null
                }
                initialCheckoutUrl={activeAttempt.checkout_url ?? null}
                initialProviderPaymentId={
                  activeAttempt.provider_payment_id ?? null
                }
                clientReportedRedirectStatus={
                  isCancelledRedirect ? "cancelled" : null
                }
                onCreateNewAttempt={handleResetAttempt}
              />
            ) : (
              <div className="space-y-4">
                <p className="text-sm font-medium">Select Payment Method</p>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  {PAYMENT_METHOD_OPTIONS.map((m) => {
                    const Icon = m.icon;
                    const active = selectedMethod === m.id;
                    return (
                      <button
                        key={m.id}
                        type="button"
                        onClick={() => setSelectedMethod(m.id)}
                        className={`flex flex-col items-start gap-1.5 rounded-lg border p-3.5 text-left text-sm transition ${
                          active
                            ? "border-primary bg-primary/5 ring-1 ring-primary/30"
                            : "hover:bg-muted/50"
                        }`}
                      >
                        <div className="flex items-center gap-2 font-semibold">
                          <Icon className="h-4 w-4 shrink-0 text-primary" />
                          <span>{m.label}</span>
                        </div>
                        <span className="text-xs leading-relaxed text-muted-foreground">
                          {m.description}
                        </span>
                      </button>
                    );
                  })}
                </div>

                <Button
                  className="w-full"
                  size="lg"
                  disabled={isInitiating || order.status === "paid"}
                  onClick={handleInitiateCheckout}
                >
                  {isInitiating ? (
                    <>
                      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                      Creating PayMongo Session...
                    </>
                  ) : (
                    <>
                      <ShieldCheck className="mr-2 h-4 w-4" />
                      Proceed to PayMongo Checkout
                    </>
                  )}
                </Button>
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}

export default function CheckoutPage() {
  return (
    <Suspense
      fallback={
        <div className="flex min-h-[60vh] items-center justify-center">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
        </div>
      }
    >
      <CheckoutPageContent />
    </Suspense>
  );
}

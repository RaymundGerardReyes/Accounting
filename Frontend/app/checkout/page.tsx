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

  const orderId = searchParams.get("order_id") || searchParams.get("orderId") || "";
  const urlAttemptId = searchParams.get("attempt_id") || searchParams.get("attemptId") || "";

  const [order, setOrder] = useState<OrderDetailDto | null>(null);
  const [activeAttempt, setActiveAttempt] =
    useState<PaymentAttemptCreatedDto | null>(null);
  const [selectedMethod, setSelectedMethod] = useState<string>("qrph");
  const [isLoadingOrder, setIsLoadingOrder] = useState<boolean>(true);
  const [isInitiating, setIsInitiating] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // 1. Restore persisted attempt from URL or sessionStorage on mount / reload
  useEffect(() => {
    if (!orderId || typeof window === "undefined") return;

    const storageKey = `${STORAGE_KEY_PREFIX}${orderId}`;
    const rawSaved = window.sessionStorage.getItem(storageKey);

    if (rawSaved) {
      try {
        const parsed = JSON.parse(rawSaved) as PaymentAttemptCreatedDto;
        if (parsed && parsed.id) {
          console.info(
            "[PAYMONGO-CHECKOUT] Restored active attempt from sessionStorage on reload:",
            parsed
          );
          setActiveAttempt(parsed);
          return;
        }
      } catch {
        // Ignore corrupted sessionStorage entry
      }
    }

    if (urlAttemptId) {
      console.info(
        "[PAYMONGO-CHECKOUT] Restored active attemptId from URL query parameter:",
        urlAttemptId
      );
      setActiveAttempt({
        id: urlAttemptId,
        order_id: orderId,
        status: "requiresaction",
        payment_method: selectedMethod,
      });
    }
  }, [orderId, urlAttemptId, selectedMethod]);

  // 2. Load Order details from GET /api/v1/orders/{order_id}
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

      // If the order response includes a latest_payment_attempt_id and we don't have one yet, mount it
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
  }, [orderId]);

  useEffect(() => {
    fetchOrder();
  }, [fetchOrder]);

  // 3. Create PayMongo Checkout Session & automatically mount <PayMongoCheckoutGuard />
  const handleInitiateCheckout = async () => {
    if (!orderId) return;

    setIsInitiating(true);
    setErrorMessage(null);

    try {
      const idempotencyKey = `idem-${orderId}-${Date.now()}`;
      console.info("[PAYMONGO-CHECKOUT] Creating new payment attempt...", {
        orderId,
        selectedMethod,
        idempotencyKey,
      });

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
          }),
        }
      );

      if (!response.ok) {
        const errText = await response.text();
        throw new Error(
          `Checkout creation failed (HTTP ${response.status}): ${errText}`
        );
      }

      const created: PaymentAttemptCreatedDto = await response.json();
      console.info(
        "[PAYMONGO-CHECKOUT] PaymentAttempt created! Mounting <PayMongoCheckoutGuard />:",
        created
      );

      // Persist in sessionStorage and URL so page reloads (F5) keep monitoring this exact attempt
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

      // Mount <PayMongoCheckoutGuard /> immediately
      setActiveAttempt(created);

      // Open PayMongo Hosted Checkout in a new tab so this page stays alive monitoring SignalR & polling
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

            {/* AUTOMATICALLY MOUNTS <PayMongoCheckoutGuard /> WHEN AN ATTEMPT EXISTS */}
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
                onCreateNewAttempt={handleResetAttempt}
                onPaymentSucceeded={() => {
                  fetchOrder();
                }}
              />
            ) : (
              <div className="space-y-4">
                <p className="text-sm font-medium">Select Payment Method</p>
                <div className="grid grid-cols-3 gap-3">
                  {[
                    { id: "qrph", label: "QR Ph", icon: QrCode },
                    { id: "gcash", label: "GCash / Maya", icon: Smartphone },
                    { id: "card", label: "Credit / Debit", icon: CreditCard },
                  ].map((m) => {
                    const Icon = m.icon;
                    const active = selectedMethod === m.id;
                    return (
                      <button
                        key={m.id}
                        type="button"
                        onClick={() => setSelectedMethod(m.id)}
                        className={`flex flex-col items-center gap-2 rounded-lg border p-3 text-sm transition ${
                          active
                            ? "border-primary bg-primary/5 font-semibold"
                            : "hover:bg-muted/50"
                        }`}
                      >
                        <Icon className="h-5 w-5" />
                        <span>{m.label}</span>
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

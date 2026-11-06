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

  const orderId = searchParams.get("order_id") || "";
  const urlAttemptId = searchParams.get("attempt_id") || "";

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
          setActiveAttempt(parsed);
          return;
        }
      } catch {
        // Ignore corrupted sessionStorage
      }
    }

    if (urlAttemptId) {
      setActiveAttempt({
        id: urlAttemptId,
        order_id: orderId,
        status: "requiresaction",
        payment_method: selectedMethod,
      });
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
                onCreateNewAttempt={handleResetAttempt}
              />
            ) : (
              <div className="space-y-4">
                <p className="text-sm font-medium">Select Payment Method</p>
                <div className="grid grid-cols-3 gap-3">
                  {([
                    { id: "qrph", label: "QR Ph", icon: QrCode },
                    { id: "gcash", label: "GCash / Maya", icon: Smartphone },
                    { id: "card", label: "Credit / Debit", icon: CreditCard },
                  ] as const).map((m) => {
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

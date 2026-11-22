"use client";

import { useEffect, useState } from "react";
import {
  fetchOrderKotobeeEntitlementsWithStatus,
  type KotobeeEntitlementDto,
  type KotobeeFulfillmentHeaderStatus,
} from "@/lib/kotobee-api";

export interface KotobeeAccessStatusCardProps {
  orderId: string;
  /** Must be true ONLY when your payment sync confirms the order is Paid. */
  isOrderPaid: boolean;
  /** Adjustable polling interval in ms (default: 2500ms). */
  pollIntervalMs?: number;
  /**
   * Adjustable maximum polling attempts while waiting for provisioning
   * before pausing automatic polling (default: 12 attempts ≈ 30s at 2500ms).
   */
  maxPollAttempts?: number;
}

export type KotobeeCardUiState =
  | "hidden"
  | "error"
  | "preparing"
  | "delayed_empty"
  | "ready";

/**
 * Pure state resolver:
 * - Stays "hidden" during the very first fetch so non-ebook orders never flicker.
 * - Immediately returns "hidden" when the backend header says "not_applicable".
 */
export function resolveKotobeeCardState(params: {
  isOrderPaid: boolean;
  orderId: string;
  error: string | null;
  hasLoadedOnce: boolean;
  entitlementsCount: number;
  fulfillmentStatus?: KotobeeFulfillmentHeaderStatus | null;
  pollExhausted: boolean;
}): KotobeeCardUiState {
  const {
    isOrderPaid,
    orderId,
    error,
    hasLoadedOnce,
    entitlementsCount,
    fulfillmentStatus = null,
    pollExhausted,
  } = params;

  if (!isOrderPaid || !orderId) return "hidden";
  if (error) return "error";
  if (entitlementsCount > 0) return "ready";

  // Wait for the first response so non-ebook orders have zero layout flash
  if (!hasLoadedOnce) return "hidden";

  // Server explicitly confirmed this order has no Kotobee ebook items
  if (fulfillmentStatus === "not_applicable") return "hidden";

  if (pollExhausted) return "delayed_empty";
  return "preparing";
}

function getStatusBadge(status: KotobeeEntitlementDto["status"]): {
  label: string;
  className: string;
} {
  switch (status) {
    case "Active":
      return {
        label: "Access Active",
        className:
          "rounded-full bg-emerald-100 dark:bg-emerald-950/50 px-2.5 py-0.5 text-xs font-medium text-emerald-800 dark:text-emerald-300",
      };
    case "Failed":
      return {
        label: "Activation Retry Needed",
        className:
          "rounded-full bg-red-100 dark:bg-red-950/50 px-2.5 py-0.5 text-xs font-medium text-red-800 dark:text-red-300",
      };
    case "Revoked":
      return {
        label: "Access Revoked",
        className:
          "rounded-full bg-slate-200 dark:bg-slate-800 px-2.5 py-0.5 text-xs font-medium text-slate-700 dark:text-slate-300",
      };
    case "Pending":
    default:
      return {
        label: "Activating…",
        className:
          "rounded-full bg-amber-100 dark:bg-amber-950/50 px-2.5 py-0.5 text-xs font-medium text-amber-800 dark:text-amber-300",
      };
  }
}

export function KotobeeAccessStatusCard({
  orderId,
  isOrderPaid,
  pollIntervalMs = 2500,
  maxPollAttempts = 12,
}: KotobeeAccessStatusCardProps) {
  const [entitlements, setEntitlements] = useState<KotobeeEntitlementDto[]>([]);
  const [fulfillmentStatus, setFulfillmentStatus] =
    useState<KotobeeFulfillmentHeaderStatus | null>(null);
  const [loading, setLoading] = useState(false);
  const [hasLoadedOnce, setHasLoadedOnce] = useState(false);
  const [pollExhausted, setPollExhausted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retryNonce, setRetryNonce] = useState(0);

  const safeMaxAttempts = Math.max(1, maxPollAttempts);

  useEffect(() => {
    // STRICT PAID-ONLY GUARD ON FRONTEND: Do nothing until payment is confirmed Paid
    if (!isOrderPaid || !orderId) return;

    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const controller = new AbortController();

    setEntitlements([]);
    setFulfillmentStatus(null);
    setHasLoadedOnce(false);
    setPollExhausted(false);
    setError(null);

    async function loadStatus(attemptNumber: number) {
      try {
        setLoading(true);
        const { entitlements: data, fulfillmentStatus: headerStatus } =
          await fetchOrderKotobeeEntitlementsWithStatus(orderId, controller.signal);

        if (cancelled) return;

        setEntitlements(data);
        setFulfillmentStatus(headerStatus);
        setError(null);
        setHasLoadedOnce(true);

        // Immediately stop polling if backend says this order has no Kotobee ebooks
        if (headerStatus === "not_applicable") {
          setPollExhausted(false);
          return;
        }

        // Keep polling while items are Pending OR waiting for worker pickup on an eligible order
        const stillProcessing =
          data.length === 0 || data.some((item) => item.status === "Pending");

        if (stillProcessing) {
          if (attemptNumber < safeMaxAttempts) {
            timer = setTimeout(() => {
              void loadStatus(attemptNumber + 1);
            }, pollIntervalMs);
          } else {
            setPollExhausted(true);
          }
        } else {
          setPollExhausted(false);
        }
      } catch (err) {
        if (!cancelled && (err as Error).name !== "AbortError") {
          setError("Unable to check ebook activation status right now.");
          setHasLoadedOnce(true);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void loadStatus(1);

    return () => {
      cancelled = true;
      controller.abort();
      if (timer) clearTimeout(timer);
    };
  }, [orderId, isOrderPaid, pollIntervalMs, safeMaxAttempts, retryNonce]);

  const uiState = resolveKotobeeCardState({
    isOrderPaid,
    orderId,
    error,
    hasLoadedOnce,
    entitlementsCount: entitlements.length,
    fulfillmentStatus,
    pollExhausted,
  });

  // Do not show this section for unpaid/pending/failed orders or missing orderId or not_applicable
  if (uiState === "hidden") return null;

  const hasPendingItems = entitlements.some((item) => item.status === "Pending");

  return (
    <section className="rounded-lg border bg-card p-5 text-card-foreground shadow-xs">
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-base font-semibold">Kotobee Ebook Access</h3>
        {loading && uiState === "preparing" && (
          <span className="text-xs text-muted-foreground">Activating access…</span>
        )}
      </div>

      {uiState === "error" && (
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-sm text-destructive">{error}</p>
          <button
            type="button"
            onClick={() => setRetryNonce((prev) => prev + 1)}
            disabled={loading}
            className="rounded-md border px-3 py-1 text-xs font-medium hover:bg-muted disabled:opacity-50"
          >
            {loading ? "Checking…" : "Retry"}
          </button>
        </div>
      )}

      {uiState === "preparing" && (
        <p className="mt-2 text-sm text-muted-foreground">
          Payment confirmed! Preparing your ebook permissions…
        </p>
      )}

      {uiState === "delayed_empty" && (
        <div className="mt-2 flex flex-wrap items-center justify-between gap-3">
          <p className="text-sm text-muted-foreground">
            No ebook permissions are ready for this order yet. If your order includes a
            Kotobee ebook, provisioning may still be finishing.
          </p>
          <button
            type="button"
            onClick={() => setRetryNonce((prev) => prev + 1)}
            disabled={loading}
            className="rounded-md border px-3 py-1.5 text-xs font-medium hover:bg-muted disabled:opacity-50"
          >
            {loading ? "Checking…" : "Check Again"}
          </button>
        </div>
      )}

      {uiState === "ready" && (
        <>
          <ul className="mt-3 divide-y divide-border">
            {entitlements.map((item) => {
              const badge = getStatusBadge(item.status);
              return (
                <li
                  key={`${item.productSku}-${item.kotobeeBookId}`}
                  className="flex items-center justify-between py-2.5 text-sm"
                >
                  <div>
                    <p className="font-medium">{item.productSku}</p>
                    <p className="text-xs text-muted-foreground">
                      Book ID: {item.kotobeeBookId}
                      {item.expiresAtUtc
                        ? ` • Expires ${new Date(item.expiresAtUtc).toLocaleDateString()}`
                        : " • Lifetime access"}
                    </p>
                  </div>

                  <span className={badge.className}>{badge.label}</span>
                </li>
              );
            })}
          </ul>

          {pollExhausted && hasPendingItems && (
            <div className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t border-border pt-3">
              <p className="text-xs text-muted-foreground">
                Activation is taking longer than usual. You can check again in a moment.
              </p>
              <button
                type="button"
                onClick={() => setRetryNonce((prev) => prev + 1)}
                disabled={loading}
                className="rounded-md border px-3 py-1 text-xs font-medium hover:bg-muted disabled:opacity-50"
              >
                {loading ? "Checking…" : "Check Again"}
              </button>
            </div>
          )}
        </>
      )}
    </section>
  );
}

export default KotobeeAccessStatusCard;

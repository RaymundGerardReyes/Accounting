"use client";

import { useEffect, useState } from "react";
import {
  fetchOrderKotobeeEntitlements,
  type KotobeeEntitlementDto,
} from "@/lib/kotobee-api";

interface KotobeeAccessStatusCardProps {
  orderId: string;
  /** Must be true ONLY when your payment sync confirms the order is Paid. */
  isOrderPaid: boolean;
  /** Optional polling interval in ms (default: 2500ms). */
  pollIntervalMs?: number;
}

export function KotobeeAccessStatusCard({
  orderId,
  isOrderPaid,
  pollIntervalMs = 2500,
}: KotobeeAccessStatusCardProps) {
  const [entitlements, setEntitlements] = useState<KotobeeEntitlementDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // STRICT PAID-ONLY GUARD ON FRONTEND: Do nothing until payment is confirmed Paid
    if (!isOrderPaid || !orderId) return;

    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const controller = new AbortController();

    async function loadStatus() {
      try {
        setLoading(true);
        const data = await fetchOrderKotobeeEntitlements(orderId, controller.signal);
        if (cancelled) return;

        setEntitlements(data);
        setError(null);

        // Keep polling only while items are still Pending (or waiting for worker pickup)
        const stillProcessing =
          data.length === 0 || data.some((item) => item.status === "Pending");

        if (stillProcessing) {
          timer = setTimeout(loadStatus, pollIntervalMs);
        }
      } catch (err) {
        if (!cancelled && (err as Error).name !== "AbortError") {
          setError("Unable to check ebook activation status right now.");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    loadStatus();

    return () => {
      cancelled = true;
      controller.abort();
      if (timer) clearTimeout(timer);
    };
  }, [orderId, isOrderPaid, pollIntervalMs]);

  // Do not show this section for unpaid/pending/failed orders
  if (!isOrderPaid) return null;

  return (
    <section className="rounded-lg border bg-card p-5 text-card-foreground shadow-xs">
      <div className="flex items-center justify-between">
        <h3 className="text-base font-semibold">Kotobee Ebook Access</h3>
        {loading && entitlements.length === 0 && (
          <span className="text-xs text-muted-foreground">Activating access…</span>
        )}
      </div>

      {error && (
        <p className="mt-2 text-sm text-destructive">{error}</p>
      )}

      {!error && entitlements.length === 0 && (
        <p className="mt-2 text-sm text-muted-foreground">
          Payment confirmed! Preparing your ebook permissions…
        </p>
      )}

      {entitlements.length > 0 && (
        <ul className="mt-3 divide-y divide-border">
          {entitlements.map((item) => (
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

              <span
                className={
                  item.status === "Active"
                    ? "rounded-full bg-emerald-100 dark:bg-emerald-950/50 px-2.5 py-0.5 text-xs font-medium text-emerald-800 dark:text-emerald-300"
                    : item.status === "Failed"
                    ? "rounded-full bg-red-100 dark:bg-red-950/50 px-2.5 py-0.5 text-xs font-medium text-red-800 dark:text-red-300"
                    : "rounded-full bg-amber-100 dark:bg-amber-950/50 px-2.5 py-0.5 text-xs font-medium text-amber-800 dark:text-amber-300"
                }
              >
                {item.status === "Active"
                  ? "Access Active"
                  : item.status === "Failed"
                  ? "Activation Retry Needed"
                  : "Activating…"}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

export default KotobeeAccessStatusCard;

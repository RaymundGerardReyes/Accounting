// File: Frontend/hooks/use-paymongo-realtime-sync.ts
"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";

export type AuthoritativePaymentStatus =
  | "pending"
  | "requiresaction"
  | "processing"
  | "succeeded"
  | "failed"
  | "expired"
  | "cancelled";

export interface ReconcileApiPayload {
  id?: string;
  attempt_id?: string;
  order_id?: string;
  provider_payment_id?: string | null;
  status: string;
  checkout_url?: string | null;
  expires_at?: string | null;
  expires_at_utc?: string | null;
  server_time_utc?: string | null;
  is_payable?: boolean;
  failure_code?: string | null;
  failure_message?: string | null;
}

export interface SyncDebugTelemetry {
  lastPollTimeIso: string | null;
  lastPollHttpStatus: number | null;
  pollCount: number;
  signalRState: string;
  serverClockOffsetMs: number;
  rawGatewayStatus: string;
  providerPaymentId: string | null;
}

export interface UsePayMongoRealtimeSyncOptions {
  attemptId: string | null | undefined;
  initialStatus?: string;
  initialExpiresAtUtc?: string | null;
  expiresAt?: string | null;
  initialCheckoutUrl?: string | null;
  checkoutUrl?: string | null;
  initialProviderPaymentId?: string | null;
  signalRHubUrl?: string;
  pollIntervalMs?: number;
  enableDebugLogs?: boolean;
  autoReconcile?: boolean;
  onStatusChange?: (newStatus: string) => void;
}

const TERMINAL_STATUSES = new Set<AuthoritativePaymentStatus>([
  "succeeded",
  "failed",
  "expired",
  "cancelled",
]);

export function normalizePaymentStatus(
  raw?: string | null
): AuthoritativePaymentStatus {
  const s = (raw ?? "").trim().toLowerCase();
  if (s === "paid" || s === "succeeded") return "succeeded";
  if (s === "failed") return "failed";
  if (s === "expired") return "expired";
  if (s === "cancelled" || s === "canceled") return "cancelled";
  if (s === "processing") return "processing";
  if (s === "requiresaction" || s === "requires_action" || s === "active") {
    return "requiresaction";
  }
  return "pending";
}

export function usePayMongoRealtimeSync({
  attemptId,
  initialStatus = "requiresaction",
  initialExpiresAtUtc = null,
  expiresAt = null,
  initialCheckoutUrl = null,
  checkoutUrl: fallbackCheckoutUrl = null,
  initialProviderPaymentId = null,
  signalRHubUrl = process.env.NEXT_PUBLIC_SIGNALR_HUB_URL || "/api/hubs/payments",
  pollIntervalMs = 3000,
  enableDebugLogs = true,
  onStatusChange,
}: UsePayMongoRealtimeSyncOptions) {
  const effectiveInitialExpiresAt = initialExpiresAtUtc ?? expiresAt ?? null;
  const effectiveInitialCheckoutUrl = initialCheckoutUrl ?? fallbackCheckoutUrl ?? null;

  const [status, setStatus] = useState<AuthoritativePaymentStatus>(() =>
    normalizePaymentStatus(initialStatus)
  );
  const [expiresAtUtc, setExpiresAtUtc] = useState<string | null>(
    effectiveInitialExpiresAt
  );
  const [checkoutUrl, setCheckoutUrl] = useState<string | null>(
    effectiveInitialCheckoutUrl
  );
  const [providerPaymentId, setProviderPaymentId] = useState<string | null>(
    initialProviderPaymentId
  );
  const [failureCode, setFailureCode] = useState<string | null>(null);
  const [failureMessage, setFailureMessage] = useState<string | null>(null);
  const [remainingSeconds, setRemainingSeconds] = useState<number>(0);
  const [isReconciling, setIsReconciling] = useState<boolean>(true);
  const [isSignalRConnected, setIsSignalRConnected] = useState<boolean>(false);

  const [telemetry, setTelemetry] = useState<SyncDebugTelemetry>({
    lastPollTimeIso: null,
    lastPollHttpStatus: null,
    pollCount: 0,
    signalRState: "Active 3s Poll",
    serverClockOffsetMs: 0,
    rawGatewayStatus: initialStatus,
    providerPaymentId: initialProviderPaymentId,
  });

  const serverClockOffsetMsRef = useRef<number>(0);
  const hasTriggeredZeroReconcileRef = useRef<boolean>(false);
  const isPollInFlightRef = useRef<boolean>(false);
  const statusRef = useRef<AuthoritativePaymentStatus>(status);
  statusRef.current = status;

  const isTerminal = TERMINAL_STATUSES.has(status);

  const logDebug = useCallback(
    (event: string, data: Record<string, unknown>) => {
      if (!enableDebugLogs || typeof window === "undefined") return;
      console.info(
        `%c[PAYMONGO-SYNC] ${event}`,
        "background: #111827; color: #f87171; font-weight: bold; padding: 2px 6px; border-radius: 4px;",
        {
          ts: new Date().toISOString(),
          attemptId,
          ...data,
        }
      );
    },
    [attemptId, enableDebugLogs]
  );

  useEffect(() => {
    const nextStatus = normalizePaymentStatus(initialStatus);
    setStatus(nextStatus);
    setExpiresAtUtc(effectiveInitialExpiresAt);
    setCheckoutUrl(effectiveInitialCheckoutUrl);
    setProviderPaymentId(initialProviderPaymentId);
    setFailureCode(null);
    setFailureMessage(null);
    hasTriggeredZeroReconcileRef.current = false;
    logDebug("HOOK_INITIALIZED_FOR_ATTEMPT", {
      attemptId,
      initialStatus: nextStatus,
      initialExpiresAtUtc: effectiveInitialExpiresAt,
      initialProviderPaymentId,
    });
  }, [
    attemptId,
    initialStatus,
    effectiveInitialExpiresAt,
    effectiveInitialCheckoutUrl,
    initialProviderPaymentId,
    logDebug,
  ]);

  const applyAuthoritativeState = useCallback(
    (
      source: string,
      nextRawStatus: string,
      nextExpiresAt?: string | null,
      nextServerTime?: string | null,
      nextCheckoutUrl?: string | null,
      nextFailureCode?: string | null,
      nextFailureMessage?: string | null,
      nextProviderId?: string | null
    ) => {
      const normalized = normalizePaymentStatus(nextRawStatus);

      if (nextServerTime) {
        const srvMs = Date.parse(nextServerTime);
        if (!Number.isNaN(srvMs)) {
          serverClockOffsetMsRef.current = srvMs - Date.now();
        }
      }

      if (nextExpiresAt) {
        setExpiresAtUtc(nextExpiresAt);
      }

      if (nextProviderId) {
        setProviderPaymentId(nextProviderId);
      }

      if (nextFailureCode) setFailureCode(nextFailureCode);
      if (nextFailureMessage) setFailureMessage(nextFailureMessage);

      setStatus((prev: AuthoritativePaymentStatus) => {
        if (TERMINAL_STATUSES.has(prev)) {
          return prev;
        }

        if (prev !== normalized) {
          logDebug("STATUS_TRANSITION", {
            source,
            previousStatus: prev,
            newStatus: normalized,
            failureCode: nextFailureCode,
            failureMessage: nextFailureMessage,
          });
          onStatusChange?.(normalized);
        }

        if (TERMINAL_STATUSES.has(normalized)) {
          setRemainingSeconds(0);
          setCheckoutUrl(null);
        } else if (nextCheckoutUrl !== undefined) {
          setCheckoutUrl(nextCheckoutUrl);
        }

        return normalized;
      });
    },
    [logDebug, onStatusChange]
  );

  const reconcileWithGateway = useCallback(
    async (triggerReason: string = "scheduled_poll") => {
      if (!attemptId || TERMINAL_STATUSES.has(statusRef.current)) {
        setIsReconciling(false);
        return;
      }

      // Prevent overlapping duplicate requests when focus + visibilitychange + interval fire together
      if (isPollInFlightRef.current && triggerReason !== "manual_debug_button") {
        return;
      }

      isPollInFlightRef.current = true;
      setIsReconciling(true);
      const encodedId = encodeURIComponent(attemptId);

      try {
        logDebug("POLL_REQUEST_START", {
          triggerReason,
          currentStatus: statusRef.current,
          endpoint: `/api/v1/payments/${attemptId}/poll`,
        });

        let response = await fetch(`/api/v1/payments/${encodedId}/poll`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          cache: "no-store",
        });

        if (response.status === 404 || response.status === 405) {
          response = await fetch(`/api/v1/payments/${encodedId}`, {
            method: "GET",
            headers: { Accept: "application/json" },
            cache: "no-store",
          });
        }

        const httpStatus = response.status;

        if (!response.ok) {
          logDebug("POLL_HTTP_ERROR", { triggerReason, httpStatus });
          setTelemetry((prev: SyncDebugTelemetry) => ({
            ...prev,
            lastPollTimeIso: new Date().toISOString(),
            lastPollHttpStatus: httpStatus,
            pollCount: prev.pollCount + 1,
          }));
          return;
        }

        const data: ReconcileApiPayload = await response.json();
        const resolvedExpiresAt = data.expires_at_utc ?? data.expires_at;
        const resolvedPayable =
          data.is_payable !== undefined
            ? data.is_payable
            : !TERMINAL_STATUSES.has(normalizePaymentStatus(data.status));

        logDebug("POLL_RESPONSE_RECEIVED", {
          triggerReason,
          httpStatus,
          rawStatus: data.status,
          providerPaymentId: data.provider_payment_id,
          isPayable: resolvedPayable,
          expiresAt: resolvedExpiresAt,
          failureCode: data.failure_code,
          failureMessage: data.failure_message,
        });

        setTelemetry((prev: SyncDebugTelemetry) => ({
          ...prev,
          lastPollTimeIso: new Date().toISOString(),
          lastPollHttpStatus: httpStatus,
          pollCount: prev.pollCount + 1,
          serverClockOffsetMs: serverClockOffsetMsRef.current,
          rawGatewayStatus: data.status,
          providerPaymentId: data.provider_payment_id ?? prev.providerPaymentId,
        }));

        applyAuthoritativeState(
          `poll:${triggerReason}`,
          data.status,
          resolvedExpiresAt,
          data.server_time_utc,
          resolvedPayable ? data.checkout_url : null,
          data.failure_code,
          data.failure_message,
          data.provider_payment_id
        );
      } catch (err) {
        logDebug("POLL_NETWORK_EXCEPTION", {
          triggerReason,
          error: err instanceof Error ? err.message : String(err),
        });
      } finally {
        isPollInFlightRef.current = false;
        setIsReconciling(false);
      }
    },
    [attemptId, applyAuthoritativeState, logDebug]
  );

  // 1. Reconcile on mount/reload and tab focus
  useEffect(() => {
    reconcileWithGateway("mount_or_reload");

    const onFocus = () => {
      reconcileWithGateway("window_focus");
    };

    const onVisibilityChange = () => {
      if (document.visibilityState === "visible") {
        reconcileWithGateway("tab_visibility_visible");
      }
    };

    window.addEventListener("focus", onFocus);
    document.addEventListener("visibilitychange", onVisibilityChange);

    return () => {
      window.removeEventListener("focus", onFocus);
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [reconcileWithGateway]);

  // 2. SignalR connection with LogLevel.None so Next.js proxy 404s never pollute console
  useEffect(() => {
    if (!attemptId || isTerminal) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(signalRHubUrl, {
        transport: signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect([0, 3000, 10000])
      .configureLogging(signalR.LogLevel.None)
      .build();

    connection.on(
      "PaymentStatusChanged",
      (event: {
        attemptId?: string;
        transactionId?: string;
        status: string;
        updatedAt?: string;
        failureCode?: string;
        failureMessage?: string;
      }) => {
        const targetId = (
          event.attemptId ??
          event.transactionId ??
          ""
        ).toLowerCase();
        const matchesAttempt = targetId === attemptId.toLowerCase();
        const matchesProviderId =
          Boolean(providerPaymentId) &&
          targetId === (providerPaymentId ?? "").toLowerCase();

        if (matchesAttempt || matchesProviderId) {
          applyAuthoritativeState(
            "signalr_push",
            event.status,
            undefined,
            event.updatedAt,
            null,
            event.failureCode,
            event.failureMessage
          );
        }
      }
    );

    connection
      .start()
      .then(() => {
        setIsSignalRConnected(true);
        setTelemetry((prev: SyncDebugTelemetry) => ({
          ...prev,
          signalRState: "Connected",
        }));
        connection.invoke("JoinPaymentGroupAsync", attemptId).catch(() => {
          connection.invoke("SubscribeToPayment", attemptId).catch(() => {});
        });
        if (providerPaymentId) {
          connection.invoke("JoinPaymentGroupAsync", providerPaymentId).catch(() => {
            connection.invoke("SubscribeToPayment", providerPaymentId).catch(() => {});
          });
        }
      })
      .catch(() => {
        setIsSignalRConnected(false);
        setTelemetry((prev: SyncDebugTelemetry) => ({
          ...prev,
          signalRState: "Active 3s Poll",
        }));
      });

    return () => {
      connection.stop().catch(() => {});
    };
  }, [
    attemptId,
    providerPaymentId,
    signalRHubUrl,
    isTerminal,
    applyAuthoritativeState,
  ]);

  // 3. Active 3-second polling loop while non-terminal
  useEffect(() => {
    if (!attemptId || isTerminal) return;

    const intervalId = setInterval(() => {
      reconcileWithGateway("interval_3s");
    }, pollIntervalMs);

    return () => clearInterval(intervalId);
  }, [attemptId, isTerminal, pollIntervalMs, reconcileWithGateway]);

  // 4. Authoritative countdown timer
  useEffect(() => {
    if (isTerminal) {
      setRemainingSeconds(0);
      return;
    }

    if (!expiresAtUtc) {
      return;
    }

    const deadlineMs = Date.parse(expiresAtUtc);
    if (Number.isNaN(deadlineMs)) {
      setRemainingSeconds(0);
      return;
    }

    const tick = () => {
      const nowAuthoritative = Date.now() + serverClockOffsetMsRef.current;
      const secs = Math.max(
        0,
        Math.ceil((deadlineMs - nowAuthoritative) / 1000)
      );
      setRemainingSeconds(secs);

      if (secs === 0 && !hasTriggeredZeroReconcileRef.current) {
        hasTriggeredZeroReconcileRef.current = true;
        reconcileWithGateway("countdown_reached_zero");
      }
    };

    tick();
    const timerId = setInterval(tick, 1000);
    return () => clearInterval(timerId);
  }, [attemptId, expiresAtUtc, isTerminal, reconcileWithGateway]);

  const minutes = Math.floor(remainingSeconds / 60);
  const seconds = remainingSeconds % 60;
  const formattedCountdown = `${String(minutes).padStart(2, "0")}:${String(
    seconds
  ).padStart(2, "0")}`;

  return {
    status,
    isTerminal,
    isPayable: !isTerminal && remainingSeconds > 0 && Boolean(checkoutUrl),
    remainingSeconds,
    formattedCountdown,
    checkoutUrl,
    providerPaymentId,
    failureCode,
    failureMessage,
    isReconciling,
    isSignalRConnected,
    telemetry,
    reconcileWithGateway,
  };
}

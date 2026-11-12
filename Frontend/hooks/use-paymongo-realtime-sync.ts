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
  clientReportedRedirectStatus?: string | null;
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
  clientReportedRedirectStatus = null,
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
  const hasSentClientRedirectStatusRef = useRef<boolean>(false);
  const isPollInFlightRef = useRef<boolean>(false);
  const currentAttemptIdRef = useRef<string | null | undefined>(undefined);
  const previousAttemptIdRef = useRef<string | null | undefined>(attemptId);
  const statusRef = useRef<AuthoritativePaymentStatus>(status);

  // Immediate synchronization during render when attemptId changes
  if (previousAttemptIdRef.current !== attemptId) {
    previousAttemptIdRef.current = attemptId;
    statusRef.current = normalizePaymentStatus(initialStatus);
  } else {
    statusRef.current = status;
  }

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
    const isNewAttempt = currentAttemptIdRef.current !== attemptId;
    currentAttemptIdRef.current = attemptId;

    if (isNewAttempt) {
      statusRef.current = nextStatus;
      isPollInFlightRef.current = false;
    }

    setStatus((prev) => {
      // Guard: If existing attempt is already succeeded, never downgrade from props
      if (!isNewAttempt && prev === "succeeded" && nextStatus !== "succeeded") {
        logDebug("IGNORED_PROP_DOWNGRADE_ON_SUCCEEDED", {
          prevStatus: prev,
          attemptedPropStatus: nextStatus,
        });
        return prev;
      }
      return nextStatus;
    });

    if (isNewAttempt) {
      setExpiresAtUtc(effectiveInitialExpiresAt);
      setCheckoutUrl(effectiveInitialCheckoutUrl);
      setProviderPaymentId(initialProviderPaymentId);
      setFailureCode(null);
      setFailureMessage(null);
      hasTriggeredZeroReconcileRef.current = false;
      hasSentClientRedirectStatusRef.current = false;
      logDebug("HOOK_INITIALIZED_FOR_ATTEMPT", {
        attemptId,
        initialStatus: nextStatus,
        initialExpiresAtUtc: effectiveInitialExpiresAt,
        initialProviderPaymentId,
      });
    }
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
        const deadlineMs = Date.parse(nextExpiresAt);
        if (!Number.isNaN(deadlineMs) && !TERMINAL_STATUSES.has(normalized)) {
          const nowAuthoritative = Date.now() + serverClockOffsetMsRef.current;
          const secs = Math.max(
            0,
            Math.ceil((deadlineMs - nowAuthoritative) / 1000)
          );
          setRemainingSeconds(secs);
        }
      }

      if (nextProviderId) {
        setProviderPaymentId(nextProviderId);
      }

      if (nextFailureCode !== undefined) setFailureCode(nextFailureCode);
      if (nextFailureMessage !== undefined) setFailureMessage(nextFailureMessage);

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

        statusRef.current = normalized;
        return normalized;
      });
    },
    [logDebug, onStatusChange]
  );

  const reconcileWithGateway = useCallback(
    async (triggerReason: string = "scheduled_poll") => {
      const targetAttemptId = attemptId;
      if (
        !targetAttemptId ||
        (currentAttemptIdRef.current !== undefined &&
          currentAttemptIdRef.current !== targetAttemptId) ||
        TERMINAL_STATUSES.has(statusRef.current)
      ) {
        setIsReconciling(false);
        return;
      }

      // Prevent overlapping duplicate requests when focus + visibilitychange + interval fire together
      if (isPollInFlightRef.current && triggerReason !== "manual_debug_button") {
        return;
      }

      isPollInFlightRef.current = true;
      setIsReconciling(true);
      const encodedId = encodeURIComponent(targetAttemptId);

      try {
        logDebug("POLL_REQUEST_START", {
          triggerReason,
          currentStatus: statusRef.current,
          endpoint: `/api/v1/payments/${targetAttemptId}/poll`,
        });

        // Pre-fetch correlation check
        if (currentAttemptIdRef.current !== targetAttemptId) return;

        const isForceTrigger =
          triggerReason === "mount_or_reload" ||
          triggerReason === "window_focus" ||
          triggerReason === "tab_visibility_visible" ||
          triggerReason === "manual_debug_button" ||
          triggerReason === "countdown_reached_zero";
        let pollQuery = isForceTrigger
          ? "?auto_reconcile=true&force_reconcile=true"
          : "?auto_reconcile=true";

        const normalizedRedirectStatus = (
          clientReportedRedirectStatus ?? ""
        )
          .trim()
          .toLowerCase();
        if (
          normalizedRedirectStatus &&
          !hasSentClientRedirectStatusRef.current
        ) {
          hasSentClientRedirectStatusRef.current = true;
          pollQuery += `&client_reported_redirect_status=${encodeURIComponent(
            normalizedRedirectStatus
          )}`;
        }

        let response = await fetch(`/api/v1/payments/${encodedId}/poll${pollQuery}`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          cache: "no-store",
        });

        // Post-primary fetch correlation check
        if (currentAttemptIdRef.current !== targetAttemptId) {
          logDebug("POLL_DROPPED_STALE_ATTEMPT", {
            step: "after_primary_fetch",
            polledAttemptId: targetAttemptId,
            currentAttemptId: currentAttemptIdRef.current,
          });
          return;
        }

        if (response.status === 404 || response.status === 405) {
          // Pre-fallback fetch correlation check
          if (currentAttemptIdRef.current !== targetAttemptId) return;

          response = await fetch(`/api/v1/payments/${encodedId}`, {
            method: "GET",
            headers: { Accept: "application/json" },
            cache: "no-store",
          });

          // Post-fallback fetch correlation check
          if (currentAttemptIdRef.current !== targetAttemptId) {
            logDebug("POLL_DROPPED_STALE_ATTEMPT", {
              step: "after_fallback_fetch",
              polledAttemptId: targetAttemptId,
              currentAttemptId: currentAttemptIdRef.current,
            });
            return;
          }
        }

        const httpStatus = response.status;

        if (!response.ok) {
          if (currentAttemptIdRef.current !== targetAttemptId) return;

          logDebug("POLL_HTTP_ERROR", { triggerReason, httpStatus });
          setTelemetry((prev: SyncDebugTelemetry) => ({
            ...prev,
            lastPollTimeIso: new Date().toISOString(),
            lastPollHttpStatus: httpStatus,
            pollCount: prev.pollCount + 1,
          }));
          return;
        }

        // Pre-json parse correlation check
        if (currentAttemptIdRef.current !== targetAttemptId) return;

        const data: ReconcileApiPayload = await response.json();

        // Post-json parse correlation check
        if (currentAttemptIdRef.current !== targetAttemptId) {
          logDebug("POLL_DROPPED_STALE_ATTEMPT", {
            step: "after_json_parse",
            polledAttemptId: targetAttemptId,
            currentAttemptId: currentAttemptIdRef.current,
          });
          return;
        }

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

        // Pre-commit correlation check
        if (currentAttemptIdRef.current !== targetAttemptId) return;

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
        if (currentAttemptIdRef.current === targetAttemptId) {
          logDebug("POLL_NETWORK_EXCEPTION", {
            triggerReason,
            error: err instanceof Error ? err.message : String(err),
          });
        }
      } finally {
        if (currentAttemptIdRef.current === targetAttemptId) {
          isPollInFlightRef.current = false;
          setIsReconciling(false);
        }
      }
    },
    [attemptId, clientReportedRedirectStatus, applyAuthoritativeState, logDebug]
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
        if (currentAttemptIdRef.current !== attemptId) {
          return;
        }

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
        if (currentAttemptIdRef.current !== attemptId) return;

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
        if (currentAttemptIdRef.current !== attemptId) return;

        setIsSignalRConnected(false);
        setTelemetry((prev: SyncDebugTelemetry) => ({
          ...prev,
          signalRState: "Active 3s Poll",
        }));
      });

    return () => {
      try {
        connection?.stop?.()?.catch?.(() => {});
      } catch (_) {}
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
      if (currentAttemptIdRef.current !== attemptId) return;

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

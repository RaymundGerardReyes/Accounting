"use client"

import { useEffect, useRef, useState, useCallback } from "react"
import * as signalR from "@microsoft/signalr"

export interface PaymentStatusPayload {
  transactionId?: string
  orderId?: string
  status?: string
  newStatus?: string
  occurredAt?: string
  [key: string]: any
}

interface UsePaymentSignalROptions {
  transactionId?: string | null | undefined
  onStatusChanged?: (payload: PaymentStatusPayload) => void
  onOrderIngested?: (payload: any) => void
  onGoogleFormsStatusUpdated?: (snapshot: any) => void
  onPollFallback?: () => void
  enabled?: boolean
}

/**
 * Real-time SignalR hook for payment status synchronization with graceful polling fallback.
 * Subscribes to the "payment:{transactionId}" group on the backend PaymentHub when a transactionId
 * is provided, or listens for global ingestion and status events across the platform.
 */
export function usePaymentSignalR({
  transactionId,
  onStatusChanged,
  onOrderIngested,
  onGoogleFormsStatusUpdated,
  onPollFallback,
  enabled = true,
}: UsePaymentSignalROptions = {}) {
  const [isConnected, setIsConnected] = useState(false)
  const [connectionError, setConnectionError] = useState<string | null>(null)
  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const statusCallbackRef = useRef(onStatusChanged)
  const orderIngestedCallbackRef = useRef(onOrderIngested)
  const formsStatusCallbackRef = useRef(onGoogleFormsStatusUpdated)
  const pollCallbackRef = useRef(onPollFallback)

  // Keep callback refs fresh without causing re-subscriptions
  useEffect(() => {
    statusCallbackRef.current = onStatusChanged
    orderIngestedCallbackRef.current = onOrderIngested
    formsStatusCallbackRef.current = onGoogleFormsStatusUpdated
    pollCallbackRef.current = onPollFallback
  }, [onStatusChanged, onOrderIngested, onGoogleFormsStatusUpdated, onPollFallback])

  // Establish and manage SignalR connection
  useEffect(() => {
    if (!enabled || typeof window === "undefined" || process.env.NODE_ENV === "test") {
      return
    }

    let isMounted = true

    const backendBase = process.env.NEXT_PUBLIC_BACKEND_URL
    // Direct Kestrel connection is only possible in local dev when hitting port 5080/5000 directly
    const isDirectKestrel = Boolean(
      backendBase &&
        (backendBase.includes(":5080") || backendBase.includes(":5000"))
    )
    const origin =
      typeof window !== "undefined" && window.location?.origin && window.location.origin !== "null"
        ? window.location.origin
        : "http://localhost:3000"

    const hubUrl = isDirectKestrel
      ? `${backendBase!.replace(/\/$/, "")}/api/hubs/payments`
      : `${origin}/api/hubs/payments`

    // When going through Next.js API route proxy (/api/hubs/payments) on Heroku or production,
    // WebSockets cannot upgrade HTTP 101 and causes Heroku router H18 503 drops.
    // LongPolling is the 100% stable, fully supported transport across the reverse proxy.
    const transport = isDirectKestrel
      ? signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling
      : signalR.HttpTransportType.LongPolling

    let connection: signalR.HubConnection
    try {
      connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
          transport,
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build()
    } catch {
      pollCallbackRef.current?.()
      return
    }

    connectionRef.current = connection

    connection.on("PaymentStatusChanged", (payload: PaymentStatusPayload) => {
      if (!isMounted) return
      if (
        !transactionId ||
        !payload ||
        !payload.transactionId ||
        payload.transactionId.toLowerCase() === transactionId.toLowerCase()
      ) {
        statusCallbackRef.current?.(payload)
      }
    })

    connection.on("PaymentStatusUpdated", (payload: any) => {
      if (!isMounted) return
      statusCallbackRef.current?.(payload)
    })

    connection.on("GoogleFormsOrderIngested", (payload: any) => {
      if (!isMounted) return
      orderIngestedCallbackRef.current?.(payload)
      statusCallbackRef.current?.(payload)
    })

    connection.on("GoogleFormsStatusUpdated", (snapshot: any) => {
      if (!isMounted) return
      formsStatusCallbackRef.current?.(snapshot)
    })

    const startConnection = async () => {
      try {
        await connection.start()
        if (!isMounted) return
        setIsConnected(true)
        setConnectionError(null)

        if (transactionId) {
          await connection.invoke("JoinPaymentGroupAsync", transactionId).catch(() => {})
        }
      } catch (err: any) {
        if (!isMounted) return
        setIsConnected(false)
        setConnectionError(err?.message || "Failed to connect to real-time payment hub")
        // Silently activate polling fallback immediately so UI keeps updating seamlessly
        pollCallbackRef.current?.()
      }
    }

    startConnection()

    connection.onreconnected(async () => {
      if (!isMounted) return
      setIsConnected(true)
      setConnectionError(null)
      try {
        if (transactionId) {
          await connection.invoke("JoinPaymentGroupAsync", transactionId).catch(() => {})
        }
      } catch {
        // Group join failure on reconnect will recover on subsequent poll
      }
    })

    connection.onclose(() => {
      if (!isMounted) return
      setIsConnected(false)
    })

    return () => {
      isMounted = false
      if (connection.state === signalR.HubConnectionState.Connected) {
        if (transactionId) {
          connection.invoke("LeavePaymentGroupAsync", transactionId).catch(() => {})
        }
        connection.stop().catch(() => {})
      } else {
        connection.stop().catch(() => {})
      }
      connectionRef.current = null
      setIsConnected(false)
    }
  }, [transactionId, enabled])

  // Polling fallback when SignalR is disconnected or unavailable
  useEffect(() => {
    if (!enabled || isConnected || process.env.NODE_ENV === "test") {
      return
    }

    // Run fallback check every 20 seconds when disconnected
    const interval = setInterval(() => {
      pollCallbackRef.current?.()
    }, 20000)

    return () => {
      ;(globalThis.clearInterval ?? window?.clearInterval)?.(interval)
    }
  }, [enabled, isConnected])

  const manualCheck = useCallback(() => {
    pollCallbackRef.current?.()
  }, [])

  return {
    isConnected,
    connectionError,
    manualCheck,
  }
}

"use client"

import { useEffect, useRef, useState, useCallback } from "react"
import * as signalR from "@microsoft/signalr"

export interface PaymentStatusPayload {
  transactionId: string
  status: string
  occurredAt?: string
}

interface UsePaymentSignalROptions {
  transactionId: string | null | undefined
  onStatusChanged?: (payload: PaymentStatusPayload) => void
  onPollFallback?: () => void
  enabled?: boolean
}

/**
 * Real-time SignalR hook for payment status synchronization with graceful polling fallback.
 * Subscribes to the "payment:{transactionId}" group on the backend PaymentHub.
 */
export function usePaymentSignalR({
  transactionId,
  onStatusChanged,
  onPollFallback,
  enabled = true,
}: UsePaymentSignalROptions) {
  const [isConnected, setIsConnected] = useState(false)
  const [connectionError, setConnectionError] = useState<string | null>(null)
  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const statusCallbackRef = useRef(onStatusChanged)
  const pollCallbackRef = useRef(onPollFallback)

  // Keep callback refs fresh without causing re-subscriptions
  useEffect(() => {
    statusCallbackRef.current = onStatusChanged
    pollCallbackRef.current = onPollFallback
  }, [onStatusChanged, onPollFallback])

  // Establish and manage SignalR connection
  useEffect(() => {
    if (!enabled || !transactionId || typeof window === "undefined") {
      return
    }

    let isMounted = true

    const backendBase = process.env.NEXT_PUBLIC_BACKEND_URL
    const isDirectBackend = Boolean(backendBase && backendBase.startsWith("http"))
    const hubUrl = isDirectBackend
      ? `${backendBase!.replace(/\/$/, "")}/api/hubs/payments`
      : "/api/hubs/payments"

    // When going through the Next.js API route proxy (/api/hubs/payments), WebSockets cannot upgrade HTTP 101.
    // Use ServerSentEvents and LongPolling for proxied connections to prevent 503 handshake aborts.
    // When connecting directly to the backend URL, WebSockets is fully supported.
    const transport = isDirectBackend
      ? signalR.HttpTransportType.WebSockets |
        signalR.HttpTransportType.ServerSentEvents |
        signalR.HttpTransportType.LongPolling
      : signalR.HttpTransportType.ServerSentEvents |
        signalR.HttpTransportType.LongPolling

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        transport,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connectionRef.current = connection

    connection.on("PaymentStatusChanged", (payload: PaymentStatusPayload) => {
      if (!isMounted) return
      if (
        payload &&
        (!payload.transactionId ||
          payload.transactionId.toLowerCase() === transactionId.toLowerCase())
      ) {
        statusCallbackRef.current?.(payload)
      }
    })

    const startConnection = async () => {
      try {
        await connection.start()
        if (!isMounted) return
        setIsConnected(true)
        setConnectionError(null)

        // Join payment transaction room
        await connection.invoke("JoinPaymentGroupAsync", transactionId)
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
        await connection.invoke("JoinPaymentGroupAsync", transactionId)
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
        connection
          .invoke("LeavePaymentGroupAsync", transactionId)
          .catch(() => {})
          .finally(() => {
            connection.stop().catch(() => {})
          })
      } else {
        connection.stop().catch(() => {})
      }
      connectionRef.current = null
      setIsConnected(false)
    }
  }, [transactionId, enabled])

  // Polling fallback when SignalR is disconnected or unavailable
  useEffect(() => {
    if (!enabled || !transactionId || isConnected) {
      return
    }

    // Run fallback check every 12 seconds
    const interval = setInterval(() => {
      pollCallbackRef.current?.()
    }, 12000)

    return () => clearInterval(interval)
  }, [enabled, transactionId, isConnected])

  const manualCheck = useCallback(() => {
    pollCallbackRef.current?.()
  }, [])

  return {
    isConnected,
    connectionError,
    manualCheck,
  }
}

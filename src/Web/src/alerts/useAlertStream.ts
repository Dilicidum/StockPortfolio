import { useEffect, useSyncExternalStore } from 'react'
import * as signalR from '@microsoft/signalr'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { API_BASE_URL, refreshAccessToken } from '../lib/apiClient'
import { getAccessExpiresAt, getAccessToken } from '../lib/tokenStore'
import {
  ALERT_HISTORY_LIMIT,
  alertKeys,
  ALERT_HUB_PATH,
  ALERT_METHOD_NAME,
  toFiredAlert,
  type AlertNotification,
  type FiredAlert,
} from './alertsApi'

export type AlertStreamStatus = 'connecting' | 'live' | 'reconnecting' | 'offline'

function readBrowserOnline(): boolean {
  return typeof navigator === 'undefined' || navigator.onLine !== false
}

let status: AlertStreamStatus = 'connecting'
let browserOnline = readBrowserOnline()
const listeners = new Set<() => void>()

function notify(): void {
  for (const listener of listeners) listener()
}

function setStatus(next: AlertStreamStatus): void {
  if (status === next) return
  status = next
  notify()
}

function setBrowserOnline(next: boolean): void {
  if (browserOnline === next) return
  browserOnline = next
  notify()
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

const getStatus = (): AlertStreamStatus => status

const getBrowserOnline = (): boolean => browserOnline

export function useAlertStreamStatus(): AlertStreamStatus {
  return useSyncExternalStore(subscribe, getStatus, getStatus)
}

export function useBrowserOnline(): boolean {
  return useSyncExternalStore(subscribe, getBrowserOnline, getBrowserOnline)
}

export function __resetAlertStream(): void {
  setStatus('connecting')
  setBrowserOnline(readBrowserOnline())
}

const RETRY_DELAYS_MS = [0, 1_000, 2_000, 5_000, 10_000, 30_000]

const OFFLINE_RECHECK_MS = 1_000

const RENEW_BEFORE_MS = 30_000

function delayFor(attempt: number): number {
  return RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)] ?? 30_000
}

export function createRetryPolicy(): signalR.IRetryPolicy {
  let skipped = 0

  return {
    nextRetryDelayInMilliseconds({ previousRetryCount }: signalR.RetryContext): number {
      if (previousRetryCount === 0) skipped = 0

      if (!readBrowserOnline()) {
        skipped += 1
        return OFFLINE_RECHECK_MS
      }

      return delayFor(previousRetryCount - skipped)
    },
  }
}

async function accessTokenFactory(): Promise<string> {
  const token = getAccessToken()
  const expiresAt = getAccessExpiresAt()

  const stale =
    token === null ||
    expiresAt === null ||
    new Date(expiresAt).getTime() - Date.now() < RENEW_BEFORE_MS

  if (!stale) return token

  try {
    return await refreshAccessToken()
  } catch {
    return ''
  }
}

function prepend(queryClient: QueryClient, alert: FiredAlert): void {
  queryClient.setQueryData<FiredAlert[]>(alertKeys.history(), (old) => {
    if (old === undefined) return old

    return [alert, ...old.filter((row) => row.id !== alert.id)].slice(0, ALERT_HISTORY_LIMIT)
  })
}

export function useAlertStream(): AlertStreamStatus {
  const queryClient = useQueryClient()

  useEffect(() => {
    let cancelled = false
    let timer: ReturnType<typeof setTimeout> | null = null
    let resumeNow: (() => void) | null = null

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL}${ALERT_HUB_PATH}`, {
        accessTokenFactory,

        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect(createRetryPolicy())
      .build()

    connection.on(ALERT_METHOD_NAME, (notification: AlertNotification) => {
      prepend(queryClient, toFiredAlert(notification))
    })

    connection.onreconnecting(() => setStatus(readBrowserOnline() ? 'reconnecting' : 'offline'))

    connection.onreconnected(() => {
      setStatus('live')

      void queryClient.invalidateQueries({ queryKey: alertKeys.history() })
    })

    connection.onclose(() => setStatus('offline'))

    function pause(ms: number): Promise<void> {
      return new Promise((resolve) => {
        resumeNow = () => {
          if (timer !== null) clearTimeout(timer)
          timer = null
          resumeNow = null
          resolve()
        }

        timer = setTimeout(() => resumeNow?.(), ms)
      })
    }

    function handleOnline(): void {
      setBrowserOnline(true)
      resumeNow?.()
    }

    function handleOffline(): void {
      setBrowserOnline(false)
      if (status !== 'live') setStatus('offline')
    }

    window.addEventListener('online', handleOnline)
    window.addEventListener('offline', handleOffline)

    setBrowserOnline(readBrowserOnline())
    setStatus(readBrowserOnline() ? 'connecting' : 'offline')

    async function connect(): Promise<void> {
      let attempt = 0

      while (!cancelled) {
        if (!readBrowserOnline()) {
          setStatus('offline')
          await pause(OFFLINE_RECHECK_MS)
          continue
        }

        try {
          await connection.start()

          if (cancelled) {
            void connection.stop()
            return
          }

          setStatus('live')
          return
        } catch {
          if (cancelled) return

          setStatus(readBrowserOnline() ? 'reconnecting' : 'offline')
          await pause(delayFor(attempt))
          attempt += 1
        }
      }
    }

    void connect()

    return () => {
      cancelled = true
      resumeNow?.()
      if (timer !== null) clearTimeout(timer)
      timer = null
      window.removeEventListener('online', handleOnline)
      window.removeEventListener('offline', handleOffline)
      void connection.stop()
    }
  }, [queryClient])

  return useAlertStreamStatus()
}

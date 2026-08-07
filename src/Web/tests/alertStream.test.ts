import { createElement, StrictMode } from 'react'
import { beforeEach, describe, expect, it } from 'vitest'
import { act, render, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { QueryClientProvider, useQuery } from '@tanstack/react-query'
import { authStore } from '../src/auth/authStore'
import { queryClient } from '../src/lib/queryClient'
import { __resetRefreshInFlight } from '../src/lib/apiClient'
import { setTokens } from '../src/lib/tokenStore'
import {
  alertHistoryQuery,
  alertKeys,
  ALERT_METHOD_NAME,
  type FiredAlert,
} from '../src/alerts/alertsApi'
import { useAlertStream } from '../src/alerts/useAlertStream'
import { FakeHubConnection } from './fakeHubConnection'
import { alertHistoryHandler, firedAlert, notificationOf } from './msw/alerts'
import { server } from './msw/server'

beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

function Probe({ withHistory = false }: { withHistory?: boolean }) {
  useAlertStream()
  if (withHistory) useQuery(alertHistoryQuery)
  return null
}

function mount(withHistory = false) {
  return render(
    createElement(
      StrictMode,
      null,
      createElement(QueryClientProvider, { client: queryClient }, createElement(Probe, { withHistory })),
    ),
  )
}

function latest(): FakeHubConnection {
  const connection = FakeHubConnection.latest()
  if (!connection) throw new Error('No hub connection was built.')
  return connection
}

describe('alert stream', () => {
  it('asks for WebSockets only and skips negotiation', async () => {
    mount()

    await waitFor(() => expect(FakeHubConnection.latest()).not.toBeNull())

    const { options } = latest()

    expect(options.transport).toBe(1)
    expect(options.skipNegotiation).toBe(true)
  })

  it('puts a pushed alert at the top of the history cache', async () => {
    queryClient.setQueryData<FiredAlert[]>(alertKeys.history(), [])

    mount()
    await waitFor(() => expect(FakeHubConnection.latest()).not.toBeNull())

    const connection = latest()
    const older = firedAlert({ ticker: 'MSFT' })
    const newer = firedAlert({ ticker: 'TSLA', direction: 'Rise', changePercent: '6.43' })

    act(() => {
      connection.push(ALERT_METHOD_NAME, notificationOf(older))
      connection.push(ALERT_METHOD_NAME, notificationOf(newer))
    })

    const history = queryClient.getQueryData<FiredAlert[]>(alertKeys.history())

    expect(history?.map((alert) => alert.ticker)).toEqual(['TSLA', 'MSFT'])

    expect(history?.[0]?.triggerPrice).toEqual({ amount: '142.0000', currency: 'USD' })
  })

  it('refetches history when a dropped connection comes back, rather than replaying', async () => {
    let historyReads = 0

    server.use(
      http.get('*/api/alerts', () => {
        historyReads += 1
        return HttpResponse.json([])
      }),
    )

    mount(true)

    await waitFor(() => expect(FakeHubConnection.latest()).not.toBeNull())
    await waitFor(() => expect(historyReads).toBeGreaterThan(0))

    await act(() => new Promise((resolve) => setTimeout(resolve, 80)))

    const readsBeforeDrop = historyReads

    act(() => latest().dropAndRecover())

    await waitFor(() => expect(historyReads).toBe(readsBeforeDrop + 1))
  })

  it('renews an expired access token before reconnecting', async () => {
    let refreshes = 0

    server.use(
      http.post('*/api/auth/refresh', () => {
        refreshes += 1
        return HttpResponse.json({ accessToken: 'renewed', refreshToken: 'next', expiresIn: 900 })
      }),
      alertHistoryHandler(),
    )

    setTokens({ accessToken: 'still-good', refreshToken: 'r', expiresIn: 900 })

    mount()
    await waitFor(() => expect(FakeHubConnection.latest()).not.toBeNull())

    const factory = latest().options.accessTokenFactory
    expect(factory).toBeDefined()

    expect(await factory!()).toBe('still-good')
    expect(refreshes).toBe(0)

    setTokens({ accessToken: 'stale', refreshToken: 'r', expiresIn: -1 })

    expect(await factory!()).toBe('renewed')
    expect(refreshes).toBe(1)
  })
})

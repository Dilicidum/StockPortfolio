import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'
import type { Money } from '../lib/format'

/**
 * The API contract, verbatim:
 *
 *   GET    /api/holdings       bearer                      -> 200 Holding[]
 *   POST   /api/holdings       {ticker,quantity,price}     -> 201 Holding (created)
 *                                                          -> 200 Holding (merged into an existing position)
 *   PATCH  /api/holdings/{id}  {quantity,price}            -> 200 Holding | 404 | 400
 *   PATCH  /api/holdings/{id}/visibility {isVisible}       -> 204 | 404
 *   DELETE /api/holdings/{id}  bearer                      -> 204 | 404
 *
 * The user is never in a request body — the bearer's `sub` claim is the owner.
 */

/**
 * Money arrives as a string so nothing here parses it as a float. The server
 * computes every monetary value; the browser only ever formats one — which is why
 * the shape and its formatter now live together in `lib/format`, shared with the
 * dashboard rather than declared a second time beside it.
 */
export type { Money } from '../lib/format'

export interface Holding {
  id: string
  ticker: string
  /**
   * The company name, from MarketData's name cache. Really arrives as `null` rather than
   * being absent, and `null` is the ordinary case rather than a failure: every position
   * added before ticker search existed has no cached name, and the cache expires weekly.
   * The row renders the ticker alone when it is missing — see `TickerCell`.
   */
  name: string | null
  quantity: number
  averagePrice: Money
  invested: Money
  isVisible: boolean
  updatedAt: string
}

export interface AddHoldingBody {
  ticker: string
  quantity: number
  price: number
}

export interface UpdateHoldingBody {
  quantity: number
  price: number
}

/** Query keys live beside the fetchers for their feature, exactly as `authKeys` does. */
export const holdingKeys = {
  all: ['holdings'] as const,
  list: () => [...holdingKeys.all, 'list'] as const,
}

export const holdingsQuery = queryOptions({
  queryKey: holdingKeys.list(),
  // The signal is what makes `cancelQueries` in the optimistic mutations actually
  // abort the in-flight GET rather than merely stop listening to it.
  queryFn: ({ signal }) => apiFetch<Holding[]>('/api/holdings', { signal }),
})

/**
 * Returns the row, plus whether it merged — the 200-vs-201 the UI announces.
 *
 * `apiFetch` returns the parsed body and discards the `Response`, so the status is
 * not reachable without widening its signature for every caller. The quantity tells
 * us instead: a merge always sums, so it always exceeds what we just submitted. If a
 * later phase needs the status itself, add an `apiFetchWithStatus` rather than
 * changing `apiFetch`.
 */
export async function addHolding(body: AddHoldingBody): Promise<{ holding: Holding; merged: boolean }> {
  const holding = await apiFetch<Holding>('/api/holdings', { method: 'POST', body })

  return { holding, merged: holding.quantity > body.quantity }
}

export const updateHolding = (id: string, body: UpdateHoldingBody): Promise<Holding> =>
  apiFetch<Holding>(`/api/holdings/${id}`, { method: 'PATCH', body })

export const removeHolding = (id: string): Promise<void> =>
  apiFetch<void>(`/api/holdings/${id}`, { method: 'DELETE' })

/** The settings screen's visibility toggle — a display filter, not a correction. */
export const setHoldingVisibility = (id: string, isVisible: boolean): Promise<void> =>
  apiFetch<void>(`/api/holdings/${id}/visibility`, { method: 'PATCH', body: { isVisible } })

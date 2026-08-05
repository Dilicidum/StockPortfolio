import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'

/**
 * The API contract, verbatim:
 *
 *   GET /api/marketdata/search?q=appl   bearer   -> 200 TickerSuggestion[]
 *
 * There is no failure shape and no error branch to render. An empty or very short `q`
 * returns `[]` without the server calling the provider, and ANY provider failure —
 * unreachable, slow, rate-limited, out of quota — also returns `[]` with a 200. So
 * "nothing matched" and "search is down" are deliberately the same response: the field
 * falls back to being the plain text box it was before this feature, which is the only
 * behaviour that lets someone record a purchase they really made during an outage.
 *
 * The provider's search is fuzzy — "AAP" returns AAPL among others — and that is wanted
 * here. It is NOT the exact-match rule the server uses to decide whether a symbol exists.
 */
export interface TickerSuggestion {
  symbol: string
  description: string
}

/** Query keys live beside the fetchers for their feature, exactly as `dashboardKeys` does. */
export const tickerSearchKeys = {
  all: ['tickerSearch'] as const,
  results: (query: string) => [...tickerSearchKeys.all, query] as const,
}

/**
 * Below this the server answers `[]` without calling the provider, so asking is a round
 * trip that cannot return anything. A one-character symbol is still perfectly typeable —
 * search is a convenience, never the way a value gets into the field.
 */
export const MIN_SEARCH_LENGTH = 2

export const tickerSearchQuery = (query: string) =>
  queryOptions({
    queryKey: tickerSearchKeys.results(query),
    // The signal is what aborts the in-flight GET when the term moves on, rather than
    // merely ignoring its answer — the same reason `holdingsQuery` takes one.
    queryFn: ({ signal }) =>
      apiFetch<TickerSuggestion[]>(`/api/marketdata/search?q=${encodeURIComponent(query)}`, { signal }),
    // Which symbols match a prefix does not change while someone fills in one form, and
    // this is a per-term key, so backspacing re-shows the previous list without a fetch.
    staleTime: 300_000,
  })

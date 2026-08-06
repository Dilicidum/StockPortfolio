import { http, HttpResponse } from 'msw'
import type { TickerSuggestion } from '../../src/marketdata/tickerSearchApi'

/**
 * The add-position form searches as you type, and `tests/setup.ts` runs MSW with
 * `onUnhandledRequest: 'error'` over a server with no default handlers — so any test that
 * types into the ticker field needs this, not only the search tests.
 *
 * Empty by default. The endpoint answers `200 []` for everything it cannot serve, so an
 * empty list IS the quiet case, and a test that wants matches says so with its own handler.
 */
export const emptyTickerSearchHandler = http.get('*/api/marketdata/search', () =>
  HttpResponse.json<TickerSuggestion[]>([]),
)

/** Matches on a case-insensitive substring of either field, the way a fuzzy provider does. */
export const tickerSearchHandler = (catalogue: TickerSuggestion[]) =>
  http.get('*/api/marketdata/search', ({ request }) => {
    const query = (new URL(request.url).searchParams.get('q') ?? '').toLowerCase()
    if (query === '') return HttpResponse.json<TickerSuggestion[]>([])

    return HttpResponse.json(
      catalogue.filter(
        (suggestion) =>
          suggestion.symbol.toLowerCase().includes(query) ||
          suggestion.description.toLowerCase().includes(query),
      ),
    )
  })

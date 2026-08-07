import { http, HttpResponse } from 'msw'
import type { TickerSuggestion } from '../../src/marketdata/tickerSearchApi'

export const emptyTickerSearchHandler = http.get('*/api/marketdata/search', () =>
  HttpResponse.json<TickerSuggestion[]>([]),
)

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

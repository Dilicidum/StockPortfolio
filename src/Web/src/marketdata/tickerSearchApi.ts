import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'

export interface TickerSuggestion {
  symbol: string
  description: string
}

export const tickerSearchKeys = {
  all: ['tickerSearch'] as const,
  results: (query: string) => [...tickerSearchKeys.all, query] as const,
}

export const MIN_SEARCH_LENGTH = 2

export const tickerSearchQuery = (query: string) =>
  queryOptions({
    queryKey: tickerSearchKeys.results(query),
    queryFn: ({ signal }) =>
      apiFetch<TickerSuggestion[]>(`/api/marketdata/search?q=${encodeURIComponent(query)}`, { signal }),
    staleTime: 300_000,
  })

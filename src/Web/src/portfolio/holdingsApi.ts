import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'
import type { Money } from '../lib/format'

export type { Money } from '../lib/format'

export interface Holding {
  id: string
  ticker: string
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

export const holdingKeys = {
  all: ['holdings'] as const,
  list: () => [...holdingKeys.all, 'list'] as const,
}

export const holdingsQuery = queryOptions({
  queryKey: holdingKeys.list(),
  queryFn: ({ signal }) => apiFetch<Holding[]>('/api/holdings', { signal }),
})

export async function addHolding(body: AddHoldingBody): Promise<{ holding: Holding; merged: boolean }> {
  const holding = await apiFetch<Holding>('/api/holdings', { method: 'POST', body })

  return { holding, merged: holding.quantity > body.quantity }
}

export const updateHolding = (id: string, body: UpdateHoldingBody): Promise<Holding> =>
  apiFetch<Holding>(`/api/holdings/${id}`, { method: 'PATCH', body })

export const removeHolding = (id: string): Promise<void> =>
  apiFetch<void>(`/api/holdings/${id}`, { method: 'DELETE' })

export const setHoldingVisibility = (id: string, isVisible: boolean): Promise<void> =>
  apiFetch<void>(`/api/holdings/${id}/visibility`, { method: 'PATCH', body: { isVisible } })

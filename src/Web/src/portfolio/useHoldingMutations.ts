import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { dashboardKeys } from '../marketdata/dashboardApi'
import {
  addHolding,
  holdingKeys,
  removeHolding,
  setHoldingVisibility,
  updateHolding,
  type AddHoldingBody,
  type Holding,
  type UpdateHoldingBody,
} from './holdingsApi'

interface Snapshot {
  previous: Holding[] | undefined
}

async function takeSnapshot(client: QueryClient): Promise<Snapshot> {
  await client.cancelQueries({ queryKey: holdingKeys.list() })
  return { previous: client.getQueryData<Holding[]>(holdingKeys.list()) }
}

function rollback(client: QueryClient, snapshot: Snapshot | undefined): void {
  if (snapshot?.previous !== undefined) {
    client.setQueryData(holdingKeys.list(), snapshot.previous)
  }
}

export function useAddHolding() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (body: AddHoldingBody) => addHolding(body),

    onSettled: () => queryClient.invalidateQueries({ queryKey: holdingKeys.list() }),
  })
}

export function useUpdateHolding() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateHoldingBody }) => updateHolding(id, body),

    onMutate: async ({ id, body }) => {
      const snapshot = await takeSnapshot(queryClient)

      queryClient.setQueryData<Holding[]>(holdingKeys.list(), (old) =>
        (old ?? []).map((holding) =>
          holding.id === id
            ? {
                ...holding,
                quantity: body.quantity,
                averagePrice: { ...holding.averagePrice, amount: String(body.price) },
              }
            : holding,
        ),
      )

      return snapshot
    },

    onError: (_error, _variables, onMutateResult) => rollback(queryClient, onMutateResult),

    onSettled: () => queryClient.invalidateQueries({ queryKey: holdingKeys.list() }),
  })
}

export function useRemoveHolding() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => removeHolding(id),

    onMutate: async (id) => {
      const snapshot = await takeSnapshot(queryClient)

      queryClient.setQueryData<Holding[]>(holdingKeys.list(), (old) =>
        (old ?? []).filter((holding) => holding.id !== id),
      )

      return snapshot
    },

    onError: (_error, _id, onMutateResult) => rollback(queryClient, onMutateResult),

    onSettled: () => queryClient.invalidateQueries({ queryKey: holdingKeys.list() }),
  })
}

export function useSetHoldingVisibility() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, isVisible }: { id: string; isVisible: boolean }) =>
      setHoldingVisibility(id, isVisible),

    onMutate: async ({ id, isVisible }) => {
      const snapshot = await takeSnapshot(queryClient)

      queryClient.setQueryData<Holding[]>(holdingKeys.list(), (old) =>
        (old ?? []).map((holding) => (holding.id === id ? { ...holding, isVisible } : holding)),
      )

      return snapshot
    },

    onError: (_error, _variables, onMutateResult) => rollback(queryClient, onMutateResult),

    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: holdingKeys.list() })
      queryClient.invalidateQueries({ queryKey: dashboardKeys.view() })
    },
  })
}

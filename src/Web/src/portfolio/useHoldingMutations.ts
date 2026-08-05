import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query'
import {
  addHolding,
  holdingKeys,
  removeHolding,
  updateHolding,
  type AddHoldingBody,
  type Holding,
  type UpdateHoldingBody,
} from './holdingsApi'

/*
 * The callback signature, read out of the installed @tanstack/react-query 5.101.4
 * type definitions rather than trusted to memory:
 *
 *   onMutate  (variables, context)
 *   onSuccess (data, variables, onMutateResult, context)
 *   onError   (error, variables, onMutateResult, context)
 *   onSettled (data, error, variables, onMutateResult, context)
 *
 * The onMutate snapshot is at position 3 in onError/onSuccess and 4 in onSettled —
 * the same positions it held before v5.89, which RENAMED the generic TContext to
 * TOnMutateResult and APPENDED a new context. It inserted nothing. Every claim that
 * pre-5.89 rollback code now restores the wrong snapshot is false.
 *
 * `cancelQueries` and `invalidateQueries` take a filters object; `getQueryData` and
 * `setQueryData` take a positional key. That split is the v5 rule — anything that can
 * match many queries takes filters.
 */

interface Snapshot {
  previous: Holding[] | undefined
}

/** Cancels in-flight reads and snapshots the list, so onError has something to restore. */
async function takeSnapshot(client: QueryClient): Promise<Snapshot> {
  await client.cancelQueries({ queryKey: holdingKeys.list() })
  return { previous: client.getQueryData<Holding[]>(holdingKeys.list()) }
}

/** Restores a snapshot taken by onMutate. It is `| undefined` because onMutate may never have run. */
function rollback(client: QueryClient, snapshot: Snapshot | undefined): void {
  if (snapshot?.previous !== undefined) {
    client.setQueryData(holdingKeys.list(), snapshot.previous)
  }
}

export function useAddHolding() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (body: AddHoldingBody) => addHolding(body),

    // No optimistic row: the server assigns the id and, on a merge, recomputes the
    // average. Guessing either would flash the one number this phase exists to get right.
    onSettled: () => queryClient.invalidateQueries({ queryKey: holdingKeys.list() }),
  })
}

export function useUpdateHolding() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateHoldingBody }) => updateHolding(id, body),

    onMutate: async ({ id, body }) => {
      const snapshot = await takeSnapshot(queryClient)

      // A correction restates the position outright, so the new average IS the price
      // typed in — nothing is being averaged and there is no server arithmetic to guess.
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

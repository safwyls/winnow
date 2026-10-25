import { useQuery, type QueryClient } from '@tanstack/react-query'
import { request } from '../api/client'
import type { MergeReview } from './parity-merge-model'

export const mergeReviewKey = ['api', 'identity.get', undefined] as const

async function readReview(signal: AbortSignal) {
  const review = await request<MergeReview>('identity.get', undefined, undefined, signal)
  if (!review || typeof review.revision !== 'string' || !Array.isArray(review.candidates))
    throw new Error('The refreshed review was incomplete. Try again.')
  return review
}

/** One authoritative cache across surfaces; hidden pages consume invalidations on entry. */
export function useIdentityReview<T extends MergeReview = MergeReview>() {
  return useQuery({
    queryKey: mergeReviewKey,
    queryFn: ({ signal }) => readReview(signal) as Promise<T>,
    retry: false,
    staleTime: Infinity,
  })
}

/** A completed sweep supersedes every older read, even if their pending counts agree. */
export async function refreshIdentityReview(client: QueryClient, signal?: AbortSignal) {
  signal?.throwIfAborted()
  await client.cancelQueries({ queryKey: mergeReviewKey, exact: true })
  signal?.throwIfAborted()
  return client.fetchQuery({
    queryKey: mergeReviewKey,
    queryFn: ({ signal: querySignal }) =>
      readReview(signal ? AbortSignal.any([querySignal, signal]) : querySignal),
    staleTime: 0,
    retry: false,
  })
}

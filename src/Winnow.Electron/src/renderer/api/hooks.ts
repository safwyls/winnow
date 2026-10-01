import {
  useQuery,
  useMutation,
  useQueryClient,
  useInfiniteQuery,
  type QueryClient,
} from '@tanstack/react-query'
import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { z } from 'zod'
import { request } from './client'
import { prepareLibrary } from './prepare-library'
import { waitForLibraryWrites } from './library-write-barrier'
import { projectLibraryHeaders } from './library-headers'
export { librarySchema } from './prepare-library'
import type {
  ActivityCursor,
  ActivityPage,
  FeedSnapshot,
  GameDetails,
  GameplayStats,
  Workspace,
  LibraryResponse,
} from './types'

const feedItemSchema = z.object({
  ownershipId: z.number(),
  releaseId: z.number(),
  title: z.string(),
  reason: z.string(),
})
export const feedSupplementSchema = z.object({
  shelves: z.array(
    z.object({
      id: z.string(),
      title: z.string(),
      blurb: z.string(),
      items: z.array(feedItemSchema),
      reserve: z.array(feedItemSchema).default([]),
      supportsFeedback: z.boolean().default(true),
    }),
  ),
  candidateCount: z.number(),
})
export const feedSchema = feedSupplementSchema.extend({
  confidence: z.number(),
  failed: z.boolean(),
})
export const workspaceSchema = z
  .object({
    works: z.array(
      z.object({ id: z.number(), name: z.string(), igdbId: z.number().nullable().optional() }).passthrough(),
    ),
    externalIds: z.array(
      z.object({ releaseId: z.number(), provider: z.string(), providerId: z.string() }).passthrough(),
    ),
    epicLaunchKeys: z.record(z.string(), z.unknown()),
    pluginActions: z.record(z.string(), z.unknown()),
  })
  .passthrough()

export function useApiQuery<T>(route: string, params?: Record<string, string | number>, enabled = true) {
  return useQuery({
    queryKey: ['api', route, params],
    queryFn: ({ signal }) => request<T>(route, params, undefined, signal),
    retry: false,
    enabled,
    staleTime: 30_000,
  })
}
export function useLibrary(enabled = true) {
  const client = useQueryClient()
  const workspace = useWorkspace(enabled)
  const select = useCallback(
    (response: LibraryResponse) => projectLibraryHeaders(response, workspace.data),
    [workspace.data],
  )
  return useQuery({
    queryKey: ['api', 'library.get'],
    queryFn: async ({ signal }) => {
      await waitForLibraryWrites(client, signal)
      return prepareLibrary(await request('library.get', undefined, undefined, signal), signal)
    },
    enabled,
    retry: false,
    staleTime: 30_000,
    select,
  })
}
export function useFeed(enabled = true) {
  const primary = useQuery({
    queryKey: ['api', 'feed.get'],
    queryFn: async ({ signal }) =>
      feedSchema.parse(await request('feed.get', undefined, undefined, signal)) as FeedSnapshot,
    enabled,
    retry: false,
    staleTime: 60_000,
  })
  // An optional shelf belongs to the completed primary pass that requested it.
  // A slow previous pass must not append stale cards after feedback or a reload.
  const supplement = useQuery({
    queryKey: ['api', 'feed.supplement', primary.dataUpdatedAt],
    queryFn: async ({ signal }) =>
      feedSupplementSchema.parse(await request('feed.supplement', undefined, undefined, signal)),
    enabled: !!primary.data && !primary.isFetching && !primary.data.failed,
    retry: false,
    staleTime: 60_000,
  })
  const additional = !primary.isFetching ? (supplement.data?.shelves ?? []) : []
  const settled = useRef<FeedSnapshot | undefined>(undefined)
  const combined = primary.data
    ? {
        ...primary.data,
        shelves: [
          ...primary.data.shelves,
          ...additional.filter((shelf) => !primary.data.shelves.some((existing) => existing.id === shelf.id)),
        ],
        candidateCount:
          primary.data.candidateCount + (!primary.isFetching ? (supplement.data?.candidateCount ?? 0) : 0),
      }
    : undefined
  useLayoutEffect(() => {
    if (!primary.isFetching && combined) settled.current = combined
  }, [primary.isFetching, combined])
  return {
    ...primary,
    data: primary.isFetching && settled.current ? settled.current : combined,
  }
}
export function useWorkspace(enabled = true) {
  return useQuery({
    queryKey: ['api', 'library.workspace', undefined],
    enabled,
    queryFn: async ({ signal }) =>
      workspaceSchema.parse(
        await request('library.workspace', undefined, undefined, signal),
      ) as unknown as Workspace,
    retry: false,
    staleTime: 30_000,
  })
}
export function useDetails(workId: number) {
  return useApiQuery<GameDetails>('game.details', { workId })
}
export function useCommand<T = unknown>() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({
      route,
      params,
      body,
    }: {
      route: string
      params?: Record<string, string | number>
      body?: unknown
    }) => request<T>(route, params, body),
    retry: false,
    onSettled: () => {
      void client.invalidateQueries({ queryKey: ['api'] })
    },
  })
}
interface ActivityRead {
  controller: AbortController
  owner: { active: boolean }
  queryKey: readonly unknown[]
}
const activityReads = new WeakMap<QueryClient, Set<ActivityRead>>()

function cancelDetachedActivityReads(client: QueryClient) {
  for (const read of activityReads.get(client) ?? []) {
    // A changed scope still owns its serialized read; an unmounted owner may have
    // handed the same cached query to another observer. Only detached reads stop.
    if (
      !read.owner.active &&
      !client.getQueryCache().find({ queryKey: read.queryKey, exact: true })?.getObserversCount()
    )
      read.controller.abort()
  }
}

export function useActivity(fromUtc: string, untilUtc: string, section: number, workId?: number) {
  // A reader may ignore cancellation. Wait for it before starting the newest scope,
  // and discard intermediate week/section requests whose signals are already aborted.
  const reading = useRef<Promise<unknown>>(Promise.resolve())
  const client = useQueryClient()
  const owner = useRef({ active: true })
  useEffect(() => {
    owner.current.active = true
    const stop = client.getQueryCache().subscribe((event) => {
      if (event.type === 'observerRemoved') queueMicrotask(() => cancelDetachedActivityReads(client))
    })
    return () => {
      owner.current.active = false
      stop()
      // useSyncExternalStore releases query observers during this same teardown.
      queueMicrotask(() => cancelDetachedActivityReads(client))
    }
  }, [client])
  const queryKey = ['api', 'activity.query', fromUtc, untilUtc, section, workId] as const
  return useInfiniteQuery({
    queryKey,
    initialPageParam: null as ActivityCursor | null,
    queryFn: ({ pageParam, signal }) => {
      const next = reading.current.then(async () => {
        signal.throwIfAborted()
        const controller = new AbortController()
        let reads = activityReads.get(client)
        if (!reads) {
          reads = new Set()
          activityReads.set(client, reads)
        }
        const read: ActivityRead = { controller, owner: owner.current, queryKey }
        reads.add(read)
        try {
          const result = await request<ActivityPage>(
            'activity.query',
            undefined,
            {
              fromUtc,
              untilUtc,
              section,
              workId,
              pageSize: 50,
              after: pageParam,
            },
            controller.signal,
          )
          signal.throwIfAborted()
          return result
        } finally {
          reads.delete(read)
        }
      })
      reading.current = next.catch(() => undefined)
      return next
    },
    getNextPageParam: (last) => last.next ?? undefined,
    retry: false,
    staleTime: 30_000,
  })
}
export function useStatistics(fromUtc: string, untilUtc: string) {
  return useQuery({
    queryKey: ['api', 'statistics.gameplay', fromUtc, untilUtc],
    queryFn: ({ signal }) =>
      request<GameplayStats>(
        'statistics.gameplay',
        undefined,
        {
          fromUtc,
          untilUtc,
          asOfUtc: untilUtc,
          timeBins: [{ fromUtc, untilUtc }],
        },
        signal,
      ),
    retry: false,
    staleTime: 30_000,
  })
}

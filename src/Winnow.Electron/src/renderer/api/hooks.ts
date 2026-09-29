import { useQuery, useMutation, useQueryClient, useInfiniteQuery } from '@tanstack/react-query'
import { useLayoutEffect, useRef } from 'react'
import { z } from 'zod'
import { request } from './client'
import type {
  ActivityCursor,
  ActivityPage,
  FeedSnapshot,
  GameDetails,
  GameplayStats,
  LibraryResponse,
  Workspace,
} from './types'

const entry = z
  .object({
    ownershipId: z.number(),
    releaseId: z.number(),
    workId: z.number(),
    title: z.string(),
    store: z.string(),
    installed: z.boolean(),
    playtimeMinutes: z.number(),
  })
  .passthrough()
const game = z
  .object({
    workId: z.number(),
    title: z.string(),
    bucket: z.string(),
    playtimeMinutes: z.number(),
    entries: z.array(entry),
  })
  .passthrough()
export const librarySchema = z
  .object({
    games: z.array(game),
    lists: z.array(
      z
        .object({
          id: z.number(),
          name: z.string(),
          isLive: z.boolean(),
          releaseIds: z.array(z.number()),
          revision: z.string(),
        })
        .passthrough(),
    ),
  })
  .passthrough()
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
    queryFn: () => request<T>(route, params),
    retry: false,
    enabled,
    staleTime: 30_000,
  })
}
export function useLibrary() {
  return useQuery({
    queryKey: ['api', 'library.get'],
    queryFn: async () => librarySchema.parse(await request('library.get')) as LibraryResponse,
    retry: false,
    staleTime: 30_000,
  })
}
export function useFeed() {
  const primary = useQuery({
    queryKey: ['api', 'feed.get'],
    queryFn: async ({ signal }) =>
      feedSchema.parse(await request('feed.get', undefined, undefined, signal)) as FeedSnapshot,
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
export function useWorkspace() {
  return useQuery({
    queryKey: ['api', 'library.workspace', undefined],
    queryFn: async () => workspaceSchema.parse(await request('library.workspace')) as unknown as Workspace,
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
export function useActivity(fromUtc: string, untilUtc: string, section: number, workId?: number) {
  return useInfiniteQuery({
    queryKey: ['api', 'activity.query', fromUtc, untilUtc, section, workId],
    initialPageParam: null as ActivityCursor | null,
    queryFn: ({ pageParam }) =>
      request<ActivityPage>('activity.query', undefined, {
        fromUtc,
        untilUtc,
        section,
        workId,
        pageSize: 50,
        after: pageParam,
      }),
    getNextPageParam: (last) => last.next ?? undefined,
    retry: false,
    staleTime: 30_000,
  })
}
export function useStatistics(fromUtc: string, untilUtc: string) {
  return useQuery({
    queryKey: ['api', 'statistics.gameplay', fromUtc, untilUtc],
    queryFn: () =>
      request<GameplayStats>('statistics.gameplay', undefined, {
        fromUtc,
        untilUtc,
        asOfUtc: untilUtc,
        timeBins: [{ fromUtc, untilUtc }],
      }),
    retry: false,
    staleTime: 30_000,
  })
}

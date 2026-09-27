import { useQuery, useMutation, useQueryClient, useInfiniteQuery } from '@tanstack/react-query'
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
export const feedSchema = z.object({
  shelves: z.array(
    z.object({
      id: z.string(),
      title: z.string(),
      blurb: z.string(),
      items: z.array(
        z.object({ ownershipId: z.number(), releaseId: z.number(), title: z.string(), reason: z.string() }),
      ),
      reserve: z.array(z.unknown()).default([]),
      supportsFeedback: z.boolean().default(true),
    }),
  ),
  candidateCount: z.number(),
  confidence: z.number(),
  failed: z.boolean(),
})

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
  return useQuery({
    queryKey: ['api', 'feed.get'],
    queryFn: async () => feedSchema.parse(await request('feed.get')) as FeedSnapshot,
    retry: false,
    staleTime: 60_000,
  })
}
export function useWorkspace() {
  return useApiQuery<Workspace>('library.workspace')
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
        pageSize: 40,
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

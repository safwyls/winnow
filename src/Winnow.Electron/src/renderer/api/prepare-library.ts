import { z } from 'zod'
import type { LibraryResponse } from './types'

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
const list = z
  .object({
    id: z.number(),
    name: z.string(),
    isLive: z.boolean(),
    releaseIds: z.array(z.number()),
    revision: z.string(),
  })
  .passthrough()
export const librarySchema = z.object({ games: z.array(game), lists: z.array(list) }).passthrough()
const envelope = z
  .object({
    games: z.custom<unknown[]>(Array.isArray),
    lists: z.custom<unknown[]>(Array.isArray),
  })
  .passthrough()
const batchSize = 128

function yieldToInput(signal?: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    const abort = () => {
      clearTimeout(timer)
      signal?.removeEventListener('abort', abort)
      reject(signal?.reason)
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', abort)
      resolve()
    }, 0)
    signal?.addEventListener('abort', abort, { once: true })
    if (signal?.aborted) abort()
  })
}

/** Keep the response private until validation finishes; input and cancellation run between batches. */
export async function prepareLibrary(value: unknown, signal?: AbortSignal): Promise<LibraryResponse> {
  signal?.throwIfAborted()
  const response = envelope.safeParse(value)
  // Preserve the public schema's diagnostics for malformed envelopes and its fast path for small libraries.
  if (!response.success || response.data.games.length + response.data.lists.length <= batchSize)
    return librarySchema.parse(value) as LibraryResponse
  const raw = response.data
  const issues: z.core.$ZodIssue[] = []
  let prepared = 0
  async function rows<T extends z.ZodType>(values: unknown[], schema: T, field: 'games' | 'lists') {
    const result: z.output<T>[] = []
    for (let index = 0; index < values.length; index++) {
      const parsed = schema.safeParse(values[index])
      if (parsed.success) result.push(parsed.data)
      else
        issues.push(
          ...parsed.error.issues.map((issue) => ({ ...issue, path: [field, index, ...issue.path] })),
        )
      if (++prepared % batchSize === 0) {
        await yieldToInput(signal)
        signal?.throwIfAborted()
      }
    }
    return result
  }
  const games = await rows(raw.games, game, 'games')
  const lists = await rows(raw.lists, list, 'lists')
  signal?.throwIfAborted()
  if (issues.length) throw new z.ZodError(issues)
  return { ...raw, games, lists } as LibraryResponse
}

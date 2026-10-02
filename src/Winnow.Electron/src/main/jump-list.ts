import { createHash } from 'node:crypto'
import { existsSync } from 'node:fs'
import { join, resolve, win32 } from 'node:path'
import type { LibraryGame, Workspace } from '../renderer/api/types'
import { primaryAction } from '../shared/game-actions'
import { quoteArgument } from './activation'
import { profileDirectory } from './storage'

/** Preserve existing taskbar pins; the legacy backend root shares the canonical installation identity. */
export function jumpListAppId(dataDirectory: string, localAppData: string, userData: string): string {
  const root = resolve(dataDirectory)
  const canonical =
    root.toLowerCase() === resolve(localAppData, 'Hoard').toLowerCase() ? join(localAppData, 'Winnow') : root
  const profileRoot = profileDirectory(userData, canonical)
  return `Winnow.Electron.${createHash('sha256').update(profileRoot.toLowerCase()).digest('hex').slice(0, 16)}`
}

export function jumpListArguments(
  dataDirectory: string | undefined,
  action: string,
  developmentEntry?: string,
): string {
  return `${developmentEntry ? `${quoteArgument(developmentEntry)} ` : ''}${action}${dataDirectory ? ` --data-dir ${quoteArgument(win32.resolve(dataDirectory))}` : ''}`
}

export interface JumpListGame {
  ownershipId: number
  title: string
  workId: number
  coverWorkId: number
  iconPath?: string
}

export function recentGames(games: LibraryGame[], workspace: Workspace) {
  return games
    .filter((game) => game.lastPlayedAt)
    .sort((left, right) => String(right.lastPlayedAt).localeCompare(String(left.lastPlayedAt)))
    .flatMap((game) => {
      const entry = game.entries.find((value) => primaryAction(value, workspace) === 'Play')
      return entry
        ? [
            {
              ownershipId: entry.ownershipId,
              title: game.title,
              workId: game.workId,
              coverWorkId: game.headerWorkId ?? game.workId,
            },
          ]
        : []
    })
    .slice(0, 10)
}

interface JumpListPublisherOptions {
  publish(games: readonly JumpListGame[]): void
  loadIcon(game: JumpListGame, signal: AbortSignal): Promise<string | null>
  exists?: (path: string) => boolean
  timeoutMs?: number
}

/** Artwork never delays the first usable tasks or republishes an obsolete snapshot. */
export class JumpListPublisher {
  private generation = 0
  private active?: AbortController
  private disposed = false
  private readonly known = new Map<number, string>()

  constructor(private readonly options: JumpListPublisherOptions) {}

  private publish(games: readonly JumpListGame[]): void {
    try {
      this.options.publish(games)
    } catch {
      // Windows may deny a custom destination category through its privacy settings.
    }
  }

  async refresh(games: readonly JumpListGame[]): Promise<void> {
    if (this.disposed) return
    const generation = ++this.generation
    this.active?.abort()
    const controller = new AbortController()
    this.active = controller
    const initial = games.map((game) => {
      const cached = this.known.get(game.coverWorkId)
      return { ...game, iconPath: cached && (this.options.exists ?? existsSync)(cached) ? cached : undefined }
    })
    this.publish(initial)
    if (!games.length) return
    const decorated: JumpListGame[] = []
    for (const game of games) {
      if (controller.signal.aborted || this.disposed || generation !== this.generation) return
      const read = new AbortController()
      const abort = () => read.abort()
      controller.signal.addEventListener('abort', abort, { once: true })
      const timeout = setTimeout(abort, this.options.timeoutMs ?? 5000)
      try {
        const iconPath = await new Promise<string | null>((resolve) => {
          const canceled = () => resolve(null)
          read.signal.addEventListener('abort', canceled, { once: true })
          Promise.resolve()
            .then(() => this.options.loadIcon(game, read.signal))
            .then(resolve, () => resolve(null))
            .finally(() => read.signal.removeEventListener('abort', canceled))
        })
        if (controller.signal.aborted || this.disposed || generation !== this.generation) return
        if (iconPath) this.known.set(game.coverWorkId, iconPath)
        else this.known.delete(game.coverWorkId)
        decorated.push({ ...game, iconPath: iconPath ?? undefined })
      } finally {
        clearTimeout(timeout)
        controller.signal.removeEventListener('abort', abort)
      }
    }
    if (!controller.signal.aborted && !this.disposed && generation === this.generation)
      this.publish(decorated)
  }

  dispose(): void {
    this.disposed = true
    ++this.generation
    this.active?.abort()
  }
}

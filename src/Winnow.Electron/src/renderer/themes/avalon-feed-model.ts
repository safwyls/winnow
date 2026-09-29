import type { AvalonShelf } from './avalon-data'

export const receiptDuration = 3000
export type FeedRow = AvalonShelf['rows'][number] & {
  pending?: boolean
  error?: string
  receipt?: { kind: number; expiresAt?: string | null; elapsed: number }
}
export type FeedDeckShelf = Omit<AvalonShelf, 'rows' | 'reserve'> & { rows: FeedRow[]; reserve: FeedRow[] }
type WriteFeedback = (
  releaseId: number,
  kind: number,
  undo: boolean,
) => Promise<{ saved: boolean; expiresAt?: string | null }>

/** A scoring refresh fills reserves; it must not remove a receipt under the reader's hand. */
export class FeedDeck {
  shelves: FeedDeckShelf[] = []
  private generation = 0
  private engaged = false
  private sourceKey = ''
  private libraryKey = ''
  private spent = new Set<number>()
  private spentWorks = new Set<number>()
  private listeners = new Set<() => void>()
  private version = 0
  private refilling = false
  private refillPending = false
  private disposed = false
  constructor(
    private write: WriteFeedback,
    private refill: () => Promise<AvalonShelf[]>,
    private changed: () => void = () => {},
  ) {}
  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }
  snapshot = () => this.version
  activate() {
    this.disposed = false
  }
  private notify() {
    this.version++
    for (const listener of this.listeners) listener()
  }
  private remember(row: FeedRow) {
    if (row.releaseId !== undefined) this.spent.add(row.releaseId)
    this.spentWorks.add(row.game.workId)
  }
  private fresh(row: FeedRow) {
    return (
      row.releaseId !== undefined && !this.spent.has(row.releaseId) && !this.spentWorks.has(row.game.workId)
    )
  }
  receive(source: AvalonShelf[], libraryKey: string, force = false, failed = false) {
    if (failed && !force && libraryKey === this.libraryKey && this.shelves.length) return
    const key = JSON.stringify(source)
    if (!force && key === this.sourceKey && libraryKey === this.libraryKey) return
    this.sourceKey = key
    if (force || !this.engaged || libraryKey !== this.libraryKey) {
      this.generation++
      this.engaged = false
      this.libraryKey = libraryKey
      this.spent.clear()
      this.spentWorks.clear()
      this.shelves = source.map((shelf) => ({
        ...shelf,
        rows: shelf.rows.map((row) => ({ ...row })),
        reserve: (shelf.reserve ?? []).map((row) => ({ ...row })),
      }))
      for (const shelf of this.shelves)
        if (shelf.feedback) for (const row of [...shelf.rows, ...shelf.reserve]) this.remember(row)
    } else this.merge(source)
    this.notify()
  }
  private merge(source: AvalonShelf[]) {
    for (const incoming of source) {
      const current = this.shelves.find((shelf) => shelf.id === incoming.id)
      if (!current) {
        const rows = [...incoming.rows, ...(incoming.reserve ?? [])].filter((row) => {
          if (incoming.feedback && !this.fresh(row)) return false
          if (incoming.feedback) this.remember(row)
          return true
        })
        if (rows.length)
          this.shelves.push({
            ...incoming,
            rows: rows.slice(0, incoming.rows.length),
            reserve: rows.slice(incoming.rows.length),
          })
      } else if (!current.feedback) {
        current.rows = incoming.rows.map((row) => ({ ...row }))
      } else {
        const games = new Map(
          [...incoming.rows, ...(incoming.reserve ?? [])].map((row) => [row.game.workId, row.game]),
        )
        for (const row of current.rows) row.game = games.get(row.game.workId) ?? row.game
        for (const row of [...incoming.rows, ...(incoming.reserve ?? [])])
          if (this.fresh(row)) {
            this.remember(row)
            current.reserve.push({ ...row })
          }
      }
    }
  }
  private live(row: FeedRow, generation: number) {
    return (
      !this.disposed &&
      generation === this.generation &&
      this.shelves.some((shelf) => shelf.rows.includes(row))
    )
  }
  async respond(row: FeedRow, kind: number, undo = false) {
    if (row.pending || row.releaseId === undefined || !this.live(row, this.generation)) return
    const generation = this.generation
    this.engaged = true
    row.pending = true
    row.error = undefined
    this.notify()
    try {
      const result = await this.write(row.releaseId, kind, undo)
      if (!this.live(row, generation)) return
      if (!undo && !result.saved) throw Error('Your choice could not be saved. Try again.')
      row.receipt = undo ? undefined : { kind, expiresAt: result.expiresAt, elapsed: 0 }
      this.changed()
    } catch (error) {
      if (this.live(row, generation))
        row.error = error instanceof Error ? error.message : 'Your choice could not be saved. Try again.'
    } finally {
      if (this.live(row, generation)) {
        row.pending = false
        this.notify()
      }
    }
  }
  restore(releaseId: number, kind: number) {
    for (const shelf of this.shelves)
      for (const row of shelf.rows)
        if (row.releaseId === releaseId && row.receipt?.kind === kind) {
          row.receipt = undefined
          row.error = undefined
        }
    this.notify()
  }
  canReplace(shelf: FeedDeckShelf) {
    const sentences = new Set(shelf.rows.map((row) => row.reason))
    return shelf.reserve.some((row) => !sentences.has(row.reason))
  }
  tick(milliseconds: number, held: (row: FeedRow) => boolean = () => false) {
    if (this.disposed || milliseconds <= 0) return
    let updated = false,
      swapped = false
    for (const shelf of this.shelves)
      for (let index = 0; index < shelf.rows.length; index++) {
        const row = shelf.rows[index]
        if (!row.receipt || row.pending || held(row) || !this.canReplace(shelf)) continue
        row.receipt.elapsed = Math.min(receiptDuration, row.receipt.elapsed + milliseconds)
        updated = true
        if (row.receipt.elapsed < receiptDuration) continue
        const sentences = new Set(shelf.rows.map((card) => card.reason))
        let replacement = shelf.reserve.shift()
        while (replacement && sentences.has(replacement.reason)) replacement = shelf.reserve.shift()
        if (replacement) {
          shelf.rows[index] = replacement
          swapped = true
        }
      }
    if (updated) this.notify()
    if (swapped) void this.backfill()
  }
  async backfill() {
    if (this.disposed) return
    if (this.refilling) {
      this.refillPending = true
      return
    }
    this.refilling = true
    try {
      do {
        this.refillPending = false
        const generation = this.generation
        try {
          const source = await this.refill()
          if (!this.disposed && generation === this.generation) {
            this.merge(source)
            this.notify()
          }
        } catch {
          /* A failed reserve refresh must leave every visible card in place. */
        }
      } while (!this.disposed && this.refillPending)
    } finally {
      this.refilling = false
    }
  }
  dispose() {
    this.disposed = true
    this.generation++
    this.listeners.clear()
  }
}

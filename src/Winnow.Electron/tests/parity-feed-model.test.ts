import { describe, expect, it, vi } from 'vitest'
import type { AvalonShelf } from '../src/renderer/themes/avalon-data'
import { FeedDeck, receiptDuration } from '../src/renderer/themes/avalon-feed-model'

function row(id: number, reason = `Reason ${id}`) {
  return {
    releaseId: id,
    reason,
    game: {
      workId: id,
      title: `Game ${id}`,
      bucket: 'never_played',
      playtimeMinutes: 0,
      entries: [
        {
          workId: id,
          releaseId: id,
          ownershipId: id,
          title: `Game ${id}`,
          store: 'steam',
          installed: false,
          playtimeMinutes: 0,
        },
      ],
    },
  }
}
function shelf(rows = [1, 2, 3, 4, 5], reserve = [6, 7], id = 'for-you'): AvalonShelf {
  return {
    id,
    title: id,
    blurb: '',
    feedback: true,
    rows: rows.map((id) => row(id)),
    reserve: reserve.map((id) => row(id)),
  }
}
function setup(source = [shelf()]) {
  const write = vi.fn(async (_id: number, kind: number, _undo: boolean) => ({
    saved: true,
    expiresAt: kind === 1 ? '2026-10-29T12:00:00Z' : null,
  }))
  const refill = vi.fn(async () => [] as AvalonShelf[]),
    changed = vi.fn()
  const deck = new FeedDeck(write, refill, changed)
  deck.receive(source, 'library')
  return { deck, write, refill, changed, card: deck.shelves[0].rows[0] }
}
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: Error) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

describe('feed receipts and reserve replacement', () => {
  it('appends optional shelves without replacing the existing shelf or card objects', () => {
    const { deck, card, write } = setup()
    const first = deck.shelves[0]
    deck.receive([shelf(), shelf([9], [], 'extra')], 'library')
    expect(deck.shelves).toHaveLength(2)
    expect(deck.shelves[0]).toBe(first)
    expect(deck.shelves[0].rows[0]).toBe(card)
    expect(write).not.toHaveBeenCalled()
  })
  it('never writes a verdict or starts replacement for an unscored recent shelf', async () => {
    const { deck, card, write } = setup([{ ...shelf(), feedback: false }])
    await deck.respond(card, 0)
    await deck.respond(card, 1)
    deck.tick(30_000)
    expect(write).not.toHaveBeenCalled()
    expect(card.receipt).toBeUndefined()
    expect(deck.shelves[0].rows[0]).toBe(card)
  })
  it('releases cards on disposal and ignores late reads, incoming snapshots and old card actions', async () => {
    const { deck, card, refill, write, changed } = setup()
    const gate = deferred<AvalonShelf[]>()
    refill.mockReturnValue(gate.promise)
    const reading = deck.backfill()
    deck.dispose()
    deck.receive([shelf([9], [10])], 'new-library')
    await deck.respond(card, 0)
    gate.resolve([shelf([1], [99])])
    await reading
    expect(deck.shelves).toEqual([])
    expect(write).not.toHaveBeenCalled()
    expect(changed).not.toHaveBeenCalled()
  })
  it('detaches the departing card from history refreshes and ignores stale swaps', async () => {
    const { deck, card, write, changed } = setup()
    await deck.respond(card, 0)
    deck.tick(3000)
    expect(deck.shelves[0].rows[0]).not.toBe(card)
    await deck.respond(card, 0, true)
    deck.tick(3000)
    expect(write).toHaveBeenCalledTimes(1)
    expect(changed).toHaveBeenCalledTimes(1)
    expect(deck.shelves[0].rows[0].releaseId).toBe(6)
  })
  it.each([0, 1])(
    'keeps the saved kind %s and the backend snooze date on the original card',
    async (kind) => {
      const { deck, write, card, changed } = setup()
      await deck.respond(card, kind)
      expect(write).toHaveBeenCalledWith(1, kind, false)
      expect(card.receipt).toEqual({
        kind,
        expiresAt: kind === 1 ? '2026-10-29T12:00:00Z' : null,
        elapsed: 0,
      })
      expect(deck.shelves[0].rows[0]).toBe(card)
      expect(changed).toHaveBeenCalledOnce()
    },
  )
  it('a failed write keeps both actions and never creates a receipt', async () => {
    const { deck, write, card, changed } = setup()
    write.mockResolvedValue({ saved: false, expiresAt: null })
    await deck.respond(card, 0)
    expect(card.receipt).toBeUndefined()
    expect(card.error).toContain('could not be saved')
    expect(card.pending).toBe(false)
    expect(changed).not.toHaveBeenCalled()
  })
  it('a thrown verdict failure becomes a retryable card error without claiming a saved receipt', async () => {
    const { deck, write, card, changed } = setup()
    write.mockRejectedValueOnce(Error('Could not save your response.'))
    await deck.respond(card, 0)
    expect(card.error).toBe('Could not save your response.')
    expect(card.receipt).toBeUndefined()
    expect(card.pending).toBe(false)
    expect(changed).not.toHaveBeenCalled()
    await deck.respond(card, 0)
    expect(card.receipt?.kind).toBe(0)
    expect(card.error).toBeUndefined()
  })
  it('duplicate presses cannot write twice while the first response is pending', async () => {
    const { deck, write, card } = setup(),
      gate = deferred<{ saved: boolean; expiresAt: null }>()
    write.mockReturnValue(gate.promise)
    const first = deck.respond(card, 0)
    await deck.respond(card, 1)
    expect(write).toHaveBeenCalledTimes(1)
    gate.resolve({ saved: true, expiresAt: null })
    await first
    expect(card.receipt?.kind).toBe(0)
  })
  it('advances a determinate three-second countdown and replaces only its original slot', async () => {
    const { deck, card } = setup(),
      unchanged = deck.shelves[0].rows[1]
    await deck.respond(card, 0)
    deck.tick(receiptDuration / 2)
    expect(card.receipt?.elapsed).toBe(1500)
    expect(deck.shelves[0].rows[0]).toBe(card)
    deck.tick(receiptDuration / 2)
    expect(deck.shelves[0].rows[0].releaseId).toBe(6)
    expect(deck.shelves[0].rows[1]).toBe(unchanged)
    expect(deck.shelves[0].rows).toHaveLength(5)
  })
  it.each(['pointer', 'focus'])(
    'holds the countdown for %s and resumes from its previous progress',
    async () => {
      const { deck, card } = setup()
      await deck.respond(card, 0)
      deck.tick(1000)
      deck.tick(6000, (current) => current === card)
      expect(card.receipt?.elapsed).toBe(1000)
      deck.tick(1999)
      expect(deck.shelves[0].rows[0]).toBe(card)
      deck.tick(1)
      expect(deck.shelves[0].rows[0].releaseId).toBe(6)
    },
  )
  it('undo stops the countdown and restores the same card and reason', async () => {
    const { deck, write, card } = setup()
    await deck.respond(card, 1)
    deck.tick(1500)
    await deck.respond(card, 1, true)
    deck.tick(30000)
    expect(write).toHaveBeenLastCalledWith(1, 1, true)
    expect(deck.shelves[0].rows[0]).toBe(card)
    expect(card.receipt).toBeUndefined()
    expect(card.reason).toBe('Reason 1')
  })
  it('a failed undo retains the existing receipt and makes retry possible', async () => {
    const { deck, write, card } = setup()
    await deck.respond(card, 0)
    write.mockRejectedValueOnce(Error('Offline'))
    await deck.respond(card, 0, true)
    expect(card.receipt?.kind).toBe(0)
    expect(card.error).toBe('Offline')
    await deck.respond(card, 0, true)
    expect(card.receipt).toBeUndefined()
    expect(card.error).toBeUndefined()
  })
  it('an already revoked or lapsed verdict restores the card even when no row changed', async () => {
    const { deck, write, card } = setup()
    await deck.respond(card, 1)
    write.mockResolvedValue({ saved: false, expiresAt: null })
    await deck.respond(card, 1, true)
    expect(card.receipt).toBeUndefined()
    expect(card.error).toBeUndefined()
  })
  it('history undo restores the matching receipt without changing another kind', async () => {
    const { deck, card } = setup()
    await deck.respond(card, 1)
    deck.restore(1, 0)
    expect(card.receipt?.kind).toBe(1)
    deck.restore(1, 1)
    expect(card.receipt).toBeUndefined()
  })
  it('keeps a receipt indefinitely when no distinct replacement exists', async () => {
    const source = shelf([1, 2], [])
    source.reserve = [row(3, 'Reason 1'), row(4, 'Reason 2')]
    const { deck, card, refill } = setup([source])
    await deck.respond(card, 0)
    deck.tick(30000)
    expect(deck.canReplace(deck.shelves[0])).toBe(false)
    expect(card.receipt?.elapsed).toBe(0)
    expect(deck.shelves[0].rows[0]).toBe(card)
    expect(refill).not.toHaveBeenCalled()
  })
  it('skips reserve sentences already shown including the outgoing card sentence', async () => {
    const source = shelf([1, 2], [])
    source.reserve = [row(3, 'Reason 1'), row(4, 'Reason 2'), row(5, 'A distinct reason')]
    const { deck, card } = setup([source])
    await deck.respond(card, 0)
    deck.tick(3000)
    expect(deck.shelves[0].rows[0].releaseId).toBe(5)
    expect(deck.shelves[0].reserve).toHaveLength(0)
  })
  it('feed invalidation preserves the visible receipt while adding unseen reserve cards', async () => {
    const { deck, card } = setup()
    await deck.respond(card, 0)
    deck.receive([shelf([2, 3, 4, 5, 6], [7, 8])], 'library')
    expect(deck.shelves[0].rows[0]).toBe(card)
    expect(deck.shelves[0].reserve.map((item) => item.releaseId)).toEqual([6, 7, 8])
  })
  it('backfill never offers a visible queued or previously answered game again', async () => {
    const { deck, card, refill } = setup()
    refill.mockResolvedValue([shelf([1, 2, 3, 4, 5], [6, 7, 8])])
    await deck.respond(card, 0)
    deck.tick(3000)
    await Promise.resolve()
    expect(deck.shelves[0].reserve.map((item) => item.releaseId)).toEqual([7, 8])
    expect(deck.shelves[0].rows.map((item) => item.releaseId)).toEqual([6, 2, 3, 4, 5])
  })
  it('backfill deduplicates scored games across shelves and across linked releases', async () => {
    const { deck, card } = setup()
    await deck.respond(card, 0)
    const linked = row(50)
    linked.game = { ...linked.game, workId: 2 }
    deck.receive([shelf(), { ...shelf([9], [], 'other'), reserve: [row(1), linked, row(10)] }], 'library')
    expect(deck.shelves[1].rows.map((item) => item.releaseId)).toEqual([9])
    expect(deck.shelves[1].reserve.map((item) => item.releaseId)).toEqual([10])
  })
  it('an old mutation cannot change a replaced library generation', async () => {
    const { deck, write, card, changed } = setup(),
      gate = deferred<{ saved: boolean; expiresAt: null }>()
    write.mockReturnValue(gate.promise)
    const saving = deck.respond(card, 0)
    deck.receive([shelf([10], [11])], 'different-library')
    gate.resolve({ saved: true, expiresAt: null })
    await saving
    expect(deck.shelves[0].rows[0].releaseId).toBe(10)
    expect(deck.shelves[0].rows[0].receipt).toBeUndefined()
    expect(changed).not.toHaveBeenCalled()
  })
  it('an old backfill cannot add cards after explicit reload or disposal', async () => {
    const { deck, refill } = setup(),
      gate = deferred<AvalonShelf[]>()
    refill.mockReturnValue(gate.promise)
    const loading = deck.backfill()
    deck.receive([shelf([10], [11])], 'library', true)
    gate.resolve([shelf([1], [99])])
    await loading
    expect(deck.shelves[0].reserve.map((item) => item.releaseId)).toEqual([11])
    const removed = deck.shelves[0].rows[0]
    deck.dispose()
    await deck.respond(removed, 0)
    expect(removed.receipt).toBeUndefined()
    expect(deck.shelves).toEqual([])
  })
  it('coalesces reserve reads to one running and one waiting even if the first fails', async () => {
    const { deck, refill } = setup(),
      gate = deferred<AvalonShelf[]>()
    refill.mockReturnValueOnce(gate.promise).mockResolvedValue([shelf([1], [8])])
    const loading = deck.backfill()
    void deck.backfill()
    void deck.backfill()
    void deck.backfill()
    expect(refill).toHaveBeenCalledTimes(1)
    gate.reject(Error('Optional provider stopped'))
    await loading
    expect(refill).toHaveBeenCalledTimes(2)
    expect(deck.shelves[0].reserve.map((item) => item.releaseId)).toContain(8)
  })
})

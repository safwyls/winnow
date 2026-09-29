import { useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError, request } from '../api/client'
import type { GameList, LibraryGame, LibraryResponse } from '../api/types'
import { cacheSavedList } from './parity-list-prompt'

export function ListOrderActions({
  list,
  games,
  selected,
  onCommitted,
}: {
  list: GameList
  games: LibraryGame[]
  selected: LibraryGame[]
  onCommitted?(): void
}) {
  const client = useQueryClient(),
    writing = useRef(false)
  const [busy, setBusy] = useState(false),
    [problem, setProblem] = useState(''),
    [blocked, setBlocked] = useState(false)
  const groups = list.releaseIds.reduce<{ key: number; ids: number[] }[]>((result, id) => {
    const game = games.find((game) => game.entries.some((entry) => entry.releaseId === id))
    const key = game?.workId ?? -id
    const group = result.find((group) => group.key === key)
    if (group) group.ids.push(id)
    else result.push({ key, ids: [id] })
    return result
  }, [])
  const index = selected.length === 1 ? groups.findIndex((group) => group.key === selected[0]!.workId) : -1
  const memberIds = list.releaseIds.filter((id) =>
    selected.some((game) => game.entries.some((entry) => entry.releaseId === id)),
  )
  async function write(kind: 'earlier' | 'later' | 'remove') {
    if (writing.current || blocked || !memberIds.length) return
    let releaseIds = memberIds
    if (kind !== 'remove') {
      const next = index + (kind === 'earlier' ? -1 : 1)
      if (index < 0 || next < 0 || next >= groups.length) return
      const ordered = [...groups]
      ;[ordered[index], ordered[next]] = [ordered[next]!, ordered[index]!]
      releaseIds = ordered.flatMap((group) => group.ids)
    }
    writing.current = true
    setBusy(true)
    setProblem('')
    try {
      await cacheSavedList(
        client,
        await request<GameList>(
          kind === 'remove' ? 'list.member.remove' : 'list.order',
          { listId: list.id },
          { releaseIds, expectedRevision: list.revision },
        ),
      )
      onCommitted?.()
    } catch (error) {
      setProblem("Couldn't save list changes. Try again.")
      if (!(error instanceof ApiError) || error.uncertain || error.conflict) setBlocked(true)
    } finally {
      writing.current = false
      setBusy(false)
    }
  }
  async function check() {
    if (writing.current) return
    writing.current = true
    setBusy(true)
    try {
      const saved = await request<LibraryResponse>('library.get')
      client.setQueryData(['api', 'library.get'], saved)
      setBlocked(false)
      setProblem('')
    } catch {
      setProblem('The saved list could not be checked. Try again.')
    } finally {
      writing.current = false
      setBusy(false)
    }
  }
  return (
    <div className="avalon-applied-filters" aria-label={`Edit ${list.name}`}>
      <span>{list.name} · Handpicked</span>
      <button disabled={busy || blocked || index <= 0} onClick={() => void write('earlier')}>
        Move earlier
      </button>
      <button
        disabled={busy || blocked || index < 0 || index >= groups.length - 1}
        onClick={() => void write('later')}
      >
        Move later
      </button>
      <button disabled={busy || blocked || !memberIds.length} onClick={() => void write('remove')}>
        Remove from {list.name}
      </button>
      {busy && <span role="status">Saving list changes…</span>}
      {problem && <span role="alert">{problem}</span>}
      {blocked && (
        <button disabled={busy} onClick={() => void check()}>
          Check saved list
        </button>
      )}
    </div>
  )
}

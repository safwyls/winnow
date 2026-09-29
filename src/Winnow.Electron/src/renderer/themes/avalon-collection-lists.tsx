import { useId, useMemo } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import type { GameList, LibraryGame } from '../api/types'
import { useWorkspace } from '../api/hooks'
import { useViewState } from '../viewState'
import { orderedLists } from '../features/parity-list-prompt'
import { avalonFacts, matchesAvalonRules, type AvalonWorkspace } from './avalon-filters'
import './avalon-collection-lists.css'

export function AvalonCollectionLists({
  lists,
  games,
  selected,
  select,
}: {
  lists: GameList[]
  games: LibraryGame[]
  selected: string | null
  select(id: string): void
}) {
  const workspace = useWorkspace()
  const facts = useMemo(
    () => avalonFacts(games, workspace.data as AvalonWorkspace | undefined),
    [games, workspace.data],
  )
  const [manualExpanded, setManualExpanded] = useViewState('avalon:collections:manual-expanded', true)
  const [liveExpanded, setLiveExpanded] = useViewState('avalon:collections:live-expanded', true)
  const id = useId()
  return (
    <div className="avalon-list-sections" role="group" aria-label="My lists">
      {[false, true].map((live) => {
        const rows = orderedLists(lists).filter((list) => list.isLive === live)
        if (live && !rows.length) return null
        const expanded = live ? liveExpanded : manualExpanded
        const change = live ? setLiveExpanded : setManualExpanded
        const controls = `${id}-${live ? 'live' : 'manual'}`
        return (
          <section key={controls}>
            <button
              className="avalon-list-heading"
              aria-expanded={expanded}
              aria-controls={controls}
              onClick={() => change(!expanded)}
            >
              {expanded ? <ChevronDown size={12} aria-hidden /> : <ChevronRight size={12} aria-hidden />}
              {live ? 'LIVE LISTS' : 'LISTS'}
            </button>
            <div id={controls} hidden={!expanded}>
              {rows.map((list) => {
                const members = new Set(list.releaseIds)
                const count = games.filter((game) =>
                  live
                    ? matchesAvalonRules(game, list.filter ?? {}, facts.get(game.workId))
                    : game.entries.some((entry) => members.has(entry.releaseId)),
                ).length
                return (
                  <button
                    key={list.id}
                    className="avalon-list-row"
                    data-avalon-list={list.id}
                    aria-label={`${list.name}, ${count} ${count === 1 ? 'game' : 'games'}`}
                    aria-pressed={selected === String(list.id)}
                    onClick={() => select(String(list.id))}
                  >
                    <span>{list.name}</span>
                    <small>{count.toLocaleString()}</small>
                  </button>
                )
              })}
            </div>
          </section>
        )
      })}
    </div>
  )
}

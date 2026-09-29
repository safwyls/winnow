import { useEffect, useRef, useState } from 'react'
import type { LibraryFilter, LibraryGame } from '../api/types'
import {
  AVALON_FACET_GROUPS,
  avalonRuleOptions,
  avalonYearRange,
  type AvalonFactMap,
  type AvalonWorkspace,
} from './avalon-filters'

export function AvalonFilterPanel({
  filter,
  games,
  facts,
  workspace,
  fullscreen,
  apply,
  close,
}: {
  filter: LibraryFilter
  games: LibraryGame[]
  facts: AvalonFactMap
  workspace?: AvalonWorkspace
  fullscreen: boolean
  apply(filter: LibraryFilter): void
  close(): void
}) {
  const [draft, setDraft] = useState(filter),
    [from, setFrom] = useState(String(filter.yearFrom ?? '')),
    [to, setTo] = useState(String(filter.yearTo ?? ''))
  const [error, setError] = useState('')
  const ref = useRef<HTMLElement>(null),
    origin = useRef(document.activeElement as HTMLElement | null)
  const valid = avalonYearRange(from, to)
  function update(next: LibraryFilter) {
    setDraft(next)
    if (!fullscreen) apply(next)
  }
  function year(nextFrom: string, nextTo: string) {
    setFrom(nextFrom)
    setTo(nextTo)
    const range = avalonYearRange(nextFrom, nextTo)
    setError(range ? '' : 'Enter years from 1000 to 9999, with the start no later than the end.')
    if (range) update({ ...draft, ...range })
  }
  useEffect(() => {
    ref.current?.querySelector<HTMLElement>('button, input, select')?.focus()
    return () => {
      origin.current?.focus()
    }
  }, [])
  const groups = [
    ['stores', '', 'Stores'],
    ['buckets', '', 'Library status'],
    ...AVALON_FACET_GROUPS,
  ] as const
  return (
    <section
      ref={ref}
      role={fullscreen ? 'dialog' : 'region'}
      aria-modal={fullscreen || undefined}
      aria-label="Library filters"
      className={`avalon-filter-panel ${fullscreen ? 'fullscreen' : ''}`}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.preventDefault()
          event.stopPropagation()
          close()
        }
        if (fullscreen && event.key === 'Tab') {
          const controls = [
            ...(ref.current?.querySelectorAll<HTMLElement>(
              'button:not(:disabled), input:not(:disabled), select:not(:disabled), summary',
            ) ?? []),
          ]
          const first = controls[0],
            last = controls.at(-1)
          if (event.shiftKey && document.activeElement === first) {
            event.preventDefault()
            last?.focus()
          } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault()
            first?.focus()
          }
        }
      }}
    >
      <header>
        <h2>Filters</h2>
        <button onClick={close}>{fullscreen ? 'Cancel' : 'Close filters'}</button>
      </header>
      <div className="avalon-filter-fields">
        <div className="avalon-filter-year">
          <label>
            From this year
            <input
              aria-label="From this year"
              inputMode="numeric"
              value={from}
              onChange={(event) => year(event.target.value, to)}
            />
          </label>
          <label>
            Up to this year
            <input
              aria-label="Up to this year"
              inputMode="numeric"
              value={to}
              onChange={(event) => year(from, event.target.value)}
            />
          </label>
        </div>
        {error && <p role="alert">{error}</p>}
        <label>
          Installation
          <select
            value={draft.installed == null ? '' : String(draft.installed)}
            onChange={(event) =>
              update({ ...draft, installed: event.target.value ? event.target.value === 'true' : null })
            }
          >
            <option value="">Any installation</option>
            <option value="true">Installed</option>
            <option value="false">Not installed</option>
          </select>
        </label>
        <label>
          Update status
          <select
            value={draft.hasUnread == null ? '' : String(draft.hasUnread)}
            onChange={(event) =>
              update({ ...draft, hasUnread: event.target.value ? event.target.value === 'true' : null })
            }
          >
            <option value="">Any update status</option>
            <option value="true">Unread updates</option>
            <option value="false">No unread updates</option>
          </select>
        </label>
        {groups.map(([key, , label]) => {
          const options = avalonRuleOptions(games, facts, workspace, draft, key),
            selected = (draft[key] ?? []) as (string | number)[]
          if (!options.length) return null
          return (
            <details key={key} open={selected.length ? true : undefined}>
              <summary>
                {label}
                {selected.length ? ` · ${selected.length}` : ''}
              </summary>
              <div className="avalon-filter-options">
                {options.map((option) => (
                  <label key={option.value}>
                    <input
                      type="checkbox"
                      aria-label={`${option.label}, ${option.count} matching ${option.count === 1 ? 'title' : 'titles'}`}
                      disabled={option.count === 0 && !selected.includes(option.value)}
                      checked={selected.includes(option.value)}
                      onChange={(event) =>
                        update({
                          ...draft,
                          [key]: event.target.checked
                            ? [...selected, option.value]
                            : selected.filter((value) => value !== option.value),
                        })
                      }
                    />
                    <span>{option.label}</span>
                    <small>{option.count}</small>
                  </label>
                ))}
              </div>
            </details>
          )
        })}
      </div>
      <footer>
        <button
          onClick={() => {
            setFrom('')
            setTo('')
            setError('')
            update({})
          }}
        >
          Clear filters
        </button>
        {fullscreen && (
          <button
            data-controller-context
            className="primary"
            disabled={!valid}
            onClick={() => {
              apply({ ...draft, ...valid })
              close()
            }}
          >
            Apply filters
          </button>
        )}
      </footer>
    </section>
  )
}

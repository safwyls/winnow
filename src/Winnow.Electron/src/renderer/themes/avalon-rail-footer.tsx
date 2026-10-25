import { useEffect, useMemo, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useQueryClient } from '@tanstack/react-query'
import { ListPlus, Settings } from 'lucide-react'
import type { ThemeContext } from '../../shared/theme'
import type { GameList, LibraryFilter } from '../api/types'
import { useLibrary, useWorkspace } from '../api/hooks'
import { CreateList } from '../features/LibraryTools'
import { cacheSavedList } from '../features/parity-list-prompt'
import { clearViewState } from '../viewState'
import { useAvalonLists } from './avalon-list-state'
import { avalonFacts, type AvalonWorkspace } from './avalon-filters'
import { libraryCutChips } from './avalon-library-chrome'
import './avalon-rail-footer.css'

const descriptions = {
  manual: 'Choose the games yourself. Add or remove titles whenever you like.',
  live: 'Save the current library filters. Matching games update automatically.',
}
export function AvalonRailFooter({ context }: { context: ThemeContext }) {
  const library = useLibrary(),
    workspace = useWorkspace(),
    client = useQueryClient()
  const state = useAvalonLists('desktop', library.data?.lists ?? [], Boolean(library.data))
  const facts = useMemo(
    () => avalonFacts(context.games, workspace.data as AvalonWorkspace | undefined),
    [context.games, workspace.data],
  )
  const [menu, setMenu] = useState(false)
  const [prompt, setPrompt] = useState<{
    kind: 'manual' | 'live'
    filter: LibraryFilter
    name: string
  } | null>(null)
  const [busy, setBusy] = useState(false),
    [created, setCreated] = useState<GameList | null>(null)
  const trigger = useRef<HTMLButtonElement>(null),
    menuRef = useRef<HTMLDivElement>(null),
    footer = useRef<HTMLElement>(null)
  const draftKey = `draft:list:rail:${prompt?.kind ?? 'manual'}`
  useEffect(() => {
    if (!menu) return
    menuRef.current?.querySelector<HTMLElement>('[role="menuitem"]')?.focus()
    const outside = (event: PointerEvent) => {
      if (!footer.current?.contains(event.target as Node)) setMenu(false)
    }
    document.addEventListener('pointerdown', outside)
    return () => document.removeEventListener('pointerdown', outside)
  }, [menu])
  useEffect(() => {
    if (!created || !state.lists.some((list) => list.id === created.id)) return
    state.selectList(String(created.id))
    setCreated(null)
    setPrompt(null)
    context.setPage('library')
  }, [created, state.lists])
  function choose(kind: 'manual' | 'live') {
    setMenu(false)
    clearViewState(`draft:list:rail:${kind}`)
    setPrompt({
      kind,
      filter: state.filter,
      name:
        kind === 'live'
          ? libraryCutChips(state, context.games, facts, workspace.data as AvalonWorkspace | undefined)
              .filter((chip) => chip.origin !== 'context')
              .slice(0, 2)
              .map((chip) => chip.label)
              .join(' · ')
              .slice(0, 200)
          : '',
    })
  }
  return (
    <footer className="avalon-rail-footer" ref={footer}>
      {menu && (
        <div
          role="menu"
          aria-label="New list"
          ref={menuRef}
          className="avalon-create-list-menu"
          onKeyDown={(event) => {
            const options = [...menuRef.current!.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')]
            const index = options.indexOf(document.activeElement as HTMLButtonElement)
            if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
              event.preventDefault()
              const next =
                event.key === 'Home'
                  ? 0
                  : event.key === 'End'
                    ? options.length - 1
                    : (index + (event.key === 'ArrowDown' ? 1 : -1) + options.length) % options.length
              options[next]?.focus()
            } else if (event.key === 'Escape' || event.key === 'Tab') {
              if (event.key === 'Escape') {
                event.preventDefault()
                event.stopPropagation()
              }
              setMenu(false)
              trigger.current?.focus()
            }
          }}
        >
          {(['manual', 'live'] as const).map((kind) => (
            <button
              key={kind}
              role="menuitem"
              title={descriptions[kind]}
              aria-description={descriptions[kind]}
              onClick={() => choose(kind)}
            >
              {kind === 'manual' ? 'Static list' : 'Live list'}
            </button>
          ))}
        </div>
      )}
      <div className="avalon-rail-footer-actions">
        <button
          ref={trigger}
          aria-haspopup="menu"
          aria-expanded={menu}
          title="Create a static or live list"
          onClick={() => setMenu(!menu)}
        >
          <ListPlus size={16} aria-hidden />
          New list
        </button>
        <button
          aria-label="Settings"
          title="Platforms, library, appearance and application"
          aria-current={context.page === 'settings' ? 'page' : undefined}
          onClick={() => {
            setMenu(false)
            context.setPage('settings')
          }}
        >
          <Settings size={16} aria-hidden />
        </button>
      </div>
      <Dialog.Root
        open={Boolean(prompt)}
        onOpenChange={(open) => {
          if (!open && !busy) {
            clearViewState(draftKey)
            setPrompt(null)
          }
        }}
      >
        {prompt && (
          <Dialog.Portal>
            <Dialog.Overlay className="dialog-overlay" />
            <Dialog.Content
              className="dialog-content feature-panel mode-desktop"
              onEscapeKeyDown={(event) => {
                if (busy) event.preventDefault()
              }}
              onPointerDownOutside={(event) => event.preventDefault()}
              onCloseAutoFocus={(event) => {
                event.preventDefault()
                trigger.current?.focus()
              }}
            >
              <Dialog.Title>{prompt.kind === 'live' ? 'Name this live list' : 'Name this list'}</Dialog.Title>
              <Dialog.Description>
                {prompt.kind === 'live'
                  ? 'It finds its own members every time your library changes.'
                  : descriptions.manual}
              </Dialog.Description>
              <CreateList
                draftKey={draftKey}
                fixedKind={prompt.kind}
                initialFilter={prompt.kind === 'live' ? prompt.filter : undefined}
                initialName={prompt.name}
                confirmLabel={prompt.kind === 'live' ? 'Save' : 'Create list'}
                onPendingChange={setBusy}
                onCreated={async (saved) => {
                  await cacheSavedList(client, saved)
                  setBusy(false)
                  if (saved.isLive) setCreated(saved)
                  else setPrompt(null)
                }}
              />
              <Dialog.Close asChild>
                <button disabled={busy}>Cancel</button>
              </Dialog.Close>
            </Dialog.Content>
          </Dialog.Portal>
        )}
      </Dialog.Root>
    </footer>
  )
}

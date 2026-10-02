import { useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useCommand, useLibrary, useWorkspace } from '../api/hooks'
import type { LibraryGame, Mode, Workspace } from '../api/types'
import { dateLabel, storeLabel } from '../api/client'
import { detailPlaytime } from './details-facts'
import { Notice } from './shared'
import dpad from './assets/xbox_dpad.svg?raw'
import selectButton from './assets/xbox_button_a_outline.svg?raw'
import backButton from './assets/xbox_button_b_outline.svg?raw'

interface Link {
  id: number
  childWorkId: number
  parentWorkId: number
  kind: string
  relationLabel?: string | null
  retractedAt?: string | null
}
type RelationshipScope = 'all' | 'expansions' | 'editions'

export function detailsRelationships(
  game: LibraryGame,
  workspace: Workspace | undefined,
  scope: RelationshipScope,
  visibleGames?: LibraryGame[],
) {
  const workIds = new Set([game.workId, ...game.entries.map((entry) => entry.workId)])
  const visibleIds =
    visibleGames &&
    new Set(visibleGames.flatMap((item) => [item.workId, ...item.entries.map((entry) => entry.workId)]))
  return ((workspace?.identityLinks ?? []) as Link[]).filter(
    (link) =>
      !link.retractedAt &&
      (!visibleIds || (visibleIds.has(link.parentWorkId) && visibleIds.has(link.childWorkId))) &&
      (workIds.has(link.parentWorkId) || workIds.has(link.childWorkId)) &&
      (scope === 'all' || (link.kind === 'expansion_of') === (scope === 'expansions')),
  )
}

export function DetailsRelationships({
  game,
  mode,
  scope = 'all',
  onOpenGame,
}: {
  game: LibraryGame
  mode: Mode
  scope?: RelationshipScope
  onOpenGame?(workId: number): void
}) {
  const workspace = useWorkspace()
  const library = useLibrary()
  const games = library.data?.games ?? []
  const links = detailsRelationships(game, workspace.data, scope, games)
  const workIds = new Set([game.workId, ...game.entries.map((entry) => entry.workId)])
  const works = (workspace.data?.works ?? []) as { id: number; name?: string; title?: string }[]
  const name = (id: number) => {
    const work = works.find((work) => work.id === id)
    return work?.name ?? work?.title ?? `Game ${id}`
  }
  if (!links.length) return null
  const groups = [
    {
      title: 'Related games & editions',
      description: null,
      links: links.filter((link) => link.kind !== 'expansion_of'),
    },
    {
      title: 'Expansions',
      description: 'Counted separately. Not added above.',
      links: links.filter((link) => link.kind === 'expansion_of' && workIds.has(link.parentWorkId)),
    },
    {
      title: 'Extends',
      description: 'A separate game, grouped for display.',
      links: links.filter((link) => link.kind === 'expansion_of' && workIds.has(link.childWorkId)),
    },
  ]
  return (
    <>
      {groups
        .filter((group) => group.links.length)
        .map((group) => (
          <section className="feature-panel details-relationships" key={group.title} aria-label={group.title}>
            <h2>{group.title}</h2>
            {group.description && <p className="relationship-description">{group.description}</p>}
            {group.links.map((link) => {
              const counterpartId = workIds.has(link.childWorkId) ? link.parentWorkId : link.childWorkId
              const counterpart = games.find(
                (item) =>
                  item.workId === counterpartId ||
                  item.entries.some((entry) => entry.workId === counterpartId),
              )
              return (
                <Relationship
                  key={link.id}
                  link={link}
                  child={name(link.childWorkId)}
                  parent={name(link.parentWorkId)}
                  mode={mode}
                  counterpart={link.kind === 'expansion_of' ? counterpart : undefined}
                  onOpenGame={onOpenGame}
                />
              )
            })}
          </section>
        ))}
    </>
  )
}
function Relationship({
  link,
  child,
  parent,
  mode,
  counterpart,
  onOpenGame,
}: {
  link: Link
  child: string
  parent: string
  mode: Mode
  counterpart?: LibraryGame
  onOpenGame?(workId: number): void
}) {
  const [open, setOpen] = useState(false),
    command = useCommand()
  const keep = useRef<HTMLButtonElement>(null)
  const relationship =
    link.kind === 'same_game'
      ? 'Another edition of'
      : link.kind === 'variant_of'
        ? 'A variant of'
        : link.relationLabel
          ? `${link.relationLabel.replaceAll('_', ' ')} of`
          : 'An expansion of'
  async function separate() {
    if (command.isPending) return
    try {
      await command.mutateAsync({
        route: 'identity.separate',
        params: { childWorkId: link.childWorkId },
        body: { expectedLinkId: link.id },
      })
      setOpen(false)
    } catch {
      /* Keep the named confirmation and the backend error available. */
    }
  }
  return (
    <article className="metadata-row details-relationship" aria-label={counterpart?.title ?? child}>
      <div>
        <h3>{counterpart?.title ?? child}</h3>
        <p>
          {relationship} {parent}
        </p>
        {counterpart && (
          <p className="relationship-facts">
            {[...new Set(counterpart.entries.map((entry) => storeLabel(entry.store)))].join(' · ')}
            {' · '}
            {detailPlaytime(counterpart.playtimeMinutes)}
            {' · Last played '}
            {counterpart.lastPlayedAt
              ? dateLabel(counterpart.lastPlayedAt)
              : counterpart.playtimeMinutes
                ? 'date not recorded'
                : 'never'}
          </p>
        )}
      </div>
      {counterpart && onOpenGame && (
        <button onClick={() => onOpenGame(counterpart.workId)}>View {counterpart.title}</button>
      )}
      <Dialog.Root
        open={open}
        onOpenChange={(value) => {
          if (!command.isPending) setOpen(value)
        }}
      >
        <Dialog.Trigger asChild>
          <button>Separate {child}…</button>
        </Dialog.Trigger>
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content
            className={`dialog-content feature-panel relationship-confirmation mode-${mode}`}
            onOpenAutoFocus={(event) => {
              event.preventDefault()
              keep.current?.focus()
            }}
            onEscapeKeyDown={(event) => {
              event.preventDefault()
              event.stopPropagation()
              if (!command.isPending) setOpen(false)
            }}
            onKeyDown={(event) => {
              if (event.key !== 'Escape') return
              event.preventDefault()
              event.stopPropagation()
              if (!command.isPending) setOpen(false)
            }}
          >
            <Dialog.Title>
              Separate {child} from {parent}?
            </Dialog.Title>
            <Dialog.Description className="relationship-description">
              Both games keep their library records. This removes the relationship between them.
            </Dialog.Description>
            <div
              className="form-actions"
              style={mode === 'fullscreen' ? { flexDirection: 'column', alignItems: 'stretch' } : undefined}
            >
              <Dialog.Close asChild>
                <button ref={keep} disabled={command.isPending}>
                  Keep relationship
                </button>
              </Dialog.Close>
              <button disabled={command.isPending} onClick={() => void separate()}>
                Separate games
              </button>
            </div>
            <Notice error={command.error} />
            {command.isPending && <p role="status">Saving the relationship…</p>}
            {mode === 'fullscreen' && (
              <div className="relationship-hints" role="group" aria-label="Relationship controls">
                {[
                  [dpad, 'D-pad', 'Choose'],
                  [selectButton, 'A', 'Select'],
                  [backButton, 'B', 'Cancel'],
                ].map(([artwork, button, label]) => (
                  <span key={button}>
                    <span
                      data-relationship-glyph={button}
                      aria-hidden="true"
                      dangerouslySetInnerHTML={{
                        __html: artwork.replace('<svg ', '<svg viewBox="8 8 48 48" '),
                      }}
                    />
                    <span className="sr-only">{button} </span>
                    {label}
                  </span>
                ))}
              </div>
            )}
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </article>
  )
}

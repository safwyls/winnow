import { useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useCommand, useWorkspace } from '../api/hooks'
import type { LibraryGame, Mode } from '../api/types'
import { Notice } from './shared'

interface Link {
  id: number
  childWorkId: number
  parentWorkId: number
  kind: string
  relationLabel?: string | null
  retractedAt?: string | null
}
export function DetailsRelationships({ game, mode }: { game: LibraryGame; mode: Mode }) {
  const workspace = useWorkspace()
  const workIds = new Set([game.workId, ...game.entries.map((entry) => entry.workId)])
  const links = ((workspace.data?.identityLinks ?? []) as Link[]).filter(
    (link) => !link.retractedAt && (workIds.has(link.parentWorkId) || workIds.has(link.childWorkId)),
  )
  const works = (workspace.data?.works ?? []) as { id: number; name?: string; title?: string }[]
  const name = (id: number) => {
    const work = works.find((work) => work.id === id)
    return work?.name ?? work?.title ?? `Game ${id}`
  }
  if (!links.length) return null
  return (
    <section className="feature-panel">
      <h2>Related games & editions</h2>
      {links.map((link) => (
        <Relationship
          key={link.id}
          link={link}
          child={name(link.childWorkId)}
          parent={name(link.parentWorkId)}
          mode={mode}
        />
      ))}
    </section>
  )
}
function Relationship({
  link,
  child,
  parent,
  mode,
}: {
  link: Link
  child: string
  parent: string
  mode: Mode
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
    <article className="metadata-row">
      <div>
        <h3>{child}</h3>
        <p>
          {relationship} {parent}
        </p>
      </div>
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
            className={`dialog-content feature-panel mode-${mode}`}
            onOpenAutoFocus={(event) => { event.preventDefault(); keep.current?.focus() }}
            onEscapeKeyDown={(event) => {
              if (command.isPending) event.preventDefault()
            }}
          >
            <Dialog.Title>
              Separate {child} from {parent}?
            </Dialog.Title>
            <Dialog.Description>
              Both games keep their library records. This removes the relationship between them.
            </Dialog.Description>
            <div className="form-actions">
              <button disabled={command.isPending} onClick={() => void separate()}>
                Separate games
              </button>
              <Dialog.Close asChild>
                <button ref={keep} disabled={command.isPending}>Keep relationship</button>
              </Dialog.Close>
            </div>
            <Notice error={command.error} />
            {command.isPending && <p role="status">Saving the relationship…</p>}
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </article>
  )
}

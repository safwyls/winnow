import type { GameEntry, Workspace } from '../api/types'
import backGlyph from './assets/xbox_button_b_outline.svg?raw'
import readGlyph from './assets/xbox_dpad.svg?raw'
import './gog-patch-notes.css'

/** The cache belongs to a store release, never an unrelated edition or a guessed product URL. */
export function cachedGogPatchNotes(entry?: GameEntry, workspace?: Workspace): string | null {
  if (entry?.store !== 'gog' || !workspace) return null
  const id = workspace.externalIds.find(
    (row) => row.releaseId === entry.releaseId && row.provider === 'gog',
  )?.providerId
  const notes = id ? workspace.storefronts?.[`gog:${id}`]?.patchNotes : null
  return notes?.trim() ? notes : null
}

export function GogPatchNotes({
  notes,
  read,
}: {
  notes: string | null
  read?(origin: HTMLButtonElement): void
}) {
  if (!notes) return null
  return read ? (
    <button
      className="gog-patch-notes-action"
      data-details-reading="Patch notes"
      onClick={(event) => read(event.currentTarget)}
    >
      Patch notes
    </button>
  ) : (
    <details className="gog-patch-notes feature-panel">
      <summary>GOG patch notes</summary>
      <GogPatchNotesText notes={notes} />
    </details>
  )
}

export function GogPatchNotesText({ notes }: { notes: string | null }) {
  return <p className="gog-patch-notes-text reading-prose">{notes}</p>
}

export function GogPatchNotesHints() {
  return (
    <div className="gog-patch-notes-hints" role="group" aria-label="Patch notes controls">
      {[
        [readGlyph, 'Up/Down', 'Read'],
        [backGlyph, 'B', 'Back'],
      ].map(([svg, key, label]) => (
        <span key={key}>
          <span
            aria-hidden="true"
            dangerouslySetInnerHTML={{ __html: svg.replace('<svg ', '<svg viewBox="8 8 48 48" ') }}
          />
          <span className="sr-only">{key} </span>
          {label}
        </span>
      ))}
    </div>
  )
}

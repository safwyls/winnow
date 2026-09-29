import { useEffect, useId, useRef, useState } from 'react'
import { ApiError, request } from '../api/client'
import { useCommand } from '../api/hooks'
import type { ManualGame } from '../api/types'
import { Notice } from './shared'
import { clearViewState, useViewState } from '../viewState'
import type { ExecutableFacts } from '../../shared/executable-facts'
import { manualFieldConflict, validateManualDraft, type ManualFieldErrors } from './manual-validation'
export function ManualEditor({
  initial,
  onClose,
  browseOnOpen = false,
  onBrowseStarted,
}: {
  initial: ManualGame | null
  onClose: () => void
  browseOnOpen?: boolean
  onBrowseStarted?: () => void
}) {
  const key = `draft:manual:${initial?.ownershipId ?? 'new'}`
  const [draft, setDraft] = useViewState(key, {
    title: initial?.title ?? '',
    year: initial?.firstReleaseYear?.toString() ?? '',
    platform: initial?.platformLabel ?? '',
    executable: initial?.executablePath ?? '',
    install: initial?.installPath ?? '',
    igdbId: initial?.igdbId?.toString() ?? '',
    steamAppId: initial?.steamAppId ?? '',
    base: initial,
    current: null as ManualGame | null,
    mappingConflict: false,
    sending: false,
    uncertain: false,
    checked: false,
    matches: [] as ManualGame[],
    proposedTitle: null as string | null,
    executableNote: null as string | null,
    matchNote: null as string | null,
  })
  const latest = useRef(draft),
    active = useRef(true),
    choosing = useRef(false),
    writing = useRef(false)
  latest.current = draft
  const [picking, setPicking] = useState(false)
  const fieldId = useId()
  const [fieldErrors, setFieldErrors] = useState<ManualFieldErrors>({})
  useEffect(() => {
    active.current = true
    return () => {
      active.current = false
    }
  }, [])
  useEffect(() => {
    if (browseOnOpen && !choosing.current) {
      onBrowseStarted?.()
      void browse()
    }
  }, [browseOnOpen])
  const { title, year, platform, base, current } = draft
  const update = (value: Partial<typeof draft>) => {
    setDraft((previous) => ({ ...previous, ...value }))
    setFieldErrors((errors) =>
      Object.fromEntries(
        Object.entries(errors).filter(
          ([field]) => !(field in value) || (draft.mappingConflict && field === 'igdbId'),
        ),
      ),
    )
  }
  const command = useCommand()
  const [checking, setChecking] = useState(false)
  const [checkError, setCheckError] = useState<unknown>(null)
  const close = () => {
    clearViewState(key)
    if (active.current) onClose()
  }
  const [candidates, setCandidates] = useState<
    { igdbId: number; name: string; firstReleaseYear?: number | null; platforms: string[] }[] | null
  >(null)
  const [searching, setSearching] = useState(false)
  async function browse() {
    if (choosing.current || latest.current.sending || latest.current.uncertain) return
    choosing.current = true
    setPicking(true)
    setCheckError(null)
    try {
      let facts: ExecutableFacts | null = null
      if (window.winnow.chooseManualExecutableFacts) facts = await window.winnow.chooseManualExecutableFacts()
      else {
        const path = await window.winnow.chooseManualExecutable?.()
        if (path)
          facts = {
            executablePath: path,
            installPath: path.replace(/[\\/][^\\/]+$/, '') || null,
            title:
              path
                .split(/[\\/]/)
                .at(-1)
                ?.replace(/\.[^.]+$/, '') || null,
            titleSource: 'file-name',
            publisher: null,
          }
      }
      if (!facts) return
      const current = latest.current
      const proposed =
        facts.title && (!current.title.trim() || current.title === current.proposedTitle)
          ? facts.title
          : current.title
      const publisher = facts.publisher ? ` Published by ${facts.publisher}.` : ''
      const note =
        facts.titleSource === 'file-description' || facts.titleSource === 'product-name'
          ? `The file identifies itself as ${facts.title}.${publisher}`
          : facts.title
            ? `Guessed ${facts.title} from the path.${publisher}`
            : 'No title found in the file. Type one above.'
      update({
        executable: facts.executablePath,
        install: facts.installPath ?? '',
        title: proposed,
        proposedTitle: proposed === facts.title ? proposed : current.proposedTitle,
        executableNote: note,
        matchNote: null,
      })
      setCandidates(null)
      if (proposed.trim()) await searchMatches(proposed)
    } catch {
      setCheckError(new Error('The executable could not be selected. Enter its path instead.'))
    } finally {
      choosing.current = false
      setPicking(false)
    }
  }
  async function searchMatches(query = title) {
    if (!query.trim() || searching) return
    setSearching(true)
    setCandidates(null)
    setCheckError(null)
    try {
      setCandidates(await request('metadata.search', { title: query }))
    } catch (error) {
      setCheckError(error)
    } finally {
      setSearching(false)
    }
  }
  async function save() {
    if (draft.uncertain || draft.sending || writing.current) return
    const errors = validateManualDraft(draft)
    setFieldErrors(errors)
    if (Object.keys(errors).length) return
    writing.current = true
    update({ sending: true })
    try {
      await command.mutateAsync({
        route: initial ? 'manual.update' : 'manual.create',
        params: initial ? { ownershipId: initial.ownershipId } : undefined,
        body: {
          title: title.trim(),
          firstReleaseYear: year.trim() ? Number(year) : null,
          platformLabel: platform || null,
          executablePath: draft.executable || null,
          installPath: draft.install || null,
          igdbId: draft.igdbId.trim() ? Number(draft.igdbId) : null,
          steamAppId: draft.steamAppId.trim() || null,
          expectedRevision: base?.revision,
          expectedIgdbMappingRevision: base?.igdbMappingRevision,
        },
      })
      close()
    } catch (error) {
      update({ sending: false })
      const fields = error instanceof ApiError && error.conflict ? manualFieldConflict(error.current) : null
      if (fields) {
        setFieldErrors(fields)
        command.reset()
      } else if (initial && error instanceof ApiError && error.conflict) {
        try {
          const all = await request<ManualGame[]>('manual.get')
          const saved = all.find((game) => game.ownershipId === initial.ownershipId) ?? null
          const mappingConflict = !!saved && saved.igdbMappingRevision !== base?.igdbMappingRevision
          update({ current: saved, mappingConflict })
          if (mappingConflict) {
            setFieldErrors(manualFieldConflict({ field: 'IgdbId', reason: 'MappingChanged' })!)
            command.reset()
          }
        } catch (failure) {
          setCheckError(failure)
        }
      } else if (!initial && (!(error instanceof ApiError) || error.uncertain))
        update({ uncertain: true, checked: false, matches: [] })
    } finally {
      writing.current = false
    }
  }
  async function reconcile() {
    setChecking(true)
    setCheckError(null)
    try {
      const saved = await request<ManualGame[]>('manual.get')
      update({
        checked: true,
        matches: saved.filter(
          (game) => game.title.trim().toLocaleLowerCase() === title.trim().toLocaleLowerCase(),
        ),
      })
    } catch (error) {
      setCheckError(error)
    } finally {
      setChecking(false)
    }
  }
  return (
    <section className="feature-panel manual-editor">
      <h2>{initial ? 'Edit game' : 'Add a game'}</h2>
      <form
        noValidate
        onSubmit={(event) => {
          event.preventDefault()
          void save().catch(() => {})
        }}
      >
        <label className="field">
          Title
          <input
            aria-label="Title"
            aria-invalid={!!fieldErrors.title}
            aria-describedby={fieldErrors.title ? `${fieldId}-title` : undefined}
            required
            maxLength={500}
            disabled={draft.sending || draft.uncertain}
            value={title}
            onChange={(event) => update({ title: event.target.value })}
          />
          {fieldErrors.title && (
            <span className="field-error" role="alert" id={`${fieldId}-title`}>
              {fieldErrors.title}
            </span>
          )}
        </label>
        <div className="form-row">
          <label className="field">
            Release year
            <input
              aria-label="Release year"
              inputMode="numeric"
              aria-invalid={!!fieldErrors.year}
              aria-describedby={fieldErrors.year ? `${fieldId}-year` : undefined}
              disabled={draft.sending || draft.uncertain}
              value={year}
              onChange={(event) => update({ year: event.target.value })}
            />
            {fieldErrors.year && (
              <span className="field-error" role="alert" id={`${fieldId}-year`}>
                {fieldErrors.year}
              </span>
            )}
          </label>
          <label className="field">
            Platform
            <input
              disabled={draft.sending || draft.uncertain}
              value={platform}
              onChange={(event) => update({ platform: event.target.value })}
              placeholder="Windows, Linux, console…"
            />
          </label>
        </div>
        <label className="field">
          Executable path
          <input
            disabled={draft.sending || draft.uncertain}
            value={draft.executable}
            onChange={(event) => update({ executable: event.target.value })}
            placeholder="Path to the game executable"
          />
        </label>
        {(window.winnow.chooseManualExecutableFacts || window.winnow.chooseManualExecutable) && (
          <button
            type="button"
            disabled={draft.sending || draft.uncertain || searching || picking}
            onClick={() => void browse()}
          >
            {picking ? 'Reading executable…' : 'Choose executable'}
          </button>
        )}
        <p className="muted">Winnow uses the executable to recognize recorded play sessions.</p>
        {draft.executableNote && <p className="muted">{draft.executableNote}</p>}
        {draft.matchNote && <p className="muted">{draft.matchNote}</p>}
        <label className="field">
          Installation folder
          <input
            disabled={draft.sending || draft.uncertain}
            value={draft.install}
            onChange={(event) => update({ install: event.target.value })}
          />
        </label>
        <div className="form-row">
          <label className="field">
            IGDB ID
            <input
              inputMode="numeric"
              aria-label="IGDB ID"
              aria-invalid={!!fieldErrors.igdbId}
              aria-describedby={fieldErrors.igdbId ? `${fieldId}-igdbId` : undefined}
              disabled={draft.sending || draft.uncertain}
              value={draft.igdbId}
              onChange={(event) => update({ igdbId: event.target.value })}
            />
            {fieldErrors.igdbId && (
              <span className="field-error" role="alert" id={`${fieldId}-igdbId`}>
                {fieldErrors.igdbId}
              </span>
            )}
          </label>
          <label className="field">
            Steam app ID
            <input
              inputMode="numeric"
              aria-label="Steam app ID"
              aria-invalid={!!fieldErrors.steamAppId}
              aria-describedby={fieldErrors.steamAppId ? `${fieldId}-steamAppId` : undefined}
              disabled={draft.sending || draft.uncertain}
              value={draft.steamAppId}
              onChange={(event) => update({ steamAppId: event.target.value })}
            />
            {fieldErrors.steamAppId && (
              <span className="field-error" role="alert" id={`${fieldId}-steamAppId`}>
                {fieldErrors.steamAppId}
              </span>
            )}
          </label>
        </div>
        <button
          type="button"
          disabled={draft.sending || draft.uncertain || searching || !title.trim()}
          onClick={() => void searchMatches()}
        >
          {searching ? 'Searching IGDB…' : 'Find IGDB matches'}
        </button>
        {candidates?.length === 0 && (
          <p className="muted">No matching games. You can still fill the form by hand.</p>
        )}
        {candidates && candidates.length > 0 && (
          <div className="igdb-candidates">
            {candidates.map((candidate) => (
              <article className="metadata-row" key={candidate.igdbId}>
                <div>
                  <strong>{candidate.name}</strong>
                  <p>
                    {[candidate.firstReleaseYear, candidate.platforms.join(', ')].filter(Boolean).join(' · ')}
                  </p>
                </div>
                <button
                  type="button"
                  disabled={draft.sending || draft.uncertain}
                  onClick={() => {
                    update({
                      title: candidate.name,
                      year: candidate.firstReleaseYear?.toString() ?? '',
                      igdbId: String(candidate.igdbId),
                      proposedTitle: candidate.name,
                      matchNote: `Using details from ${candidate.name}. Nothing is saved until you choose Save game.`,
                    })
                    setCandidates(null)
                  }}
                >
                  Use these details
                </button>
              </article>
            ))}
            <button type="button" onClick={() => setCandidates(null)}>
              Keep my own details
            </button>
          </div>
        )}
        <Notice error={command.error} />
        <Notice error={checkError} />
        {current && (
          <div className="conflict-panel">
            <p>The saved entry changed to “{current.title}”. Your draft is preserved.</p>
            {!draft.mappingConflict && (
              <button
                type="button"
                onClick={() => {
                  update({ base: current, current: null })
                  command.reset()
                }}
              >
                Keep my draft for the next save
              </button>
            )}
            <button type="button" onClick={close}>
              Use saved entry
            </button>
          </div>
        )}
        {draft.uncertain && (
          <section className="conflict-panel">
            <h3>Check whether this game was added</h3>
            <p>
              The response was interrupted. Your draft is kept here; adding it again could create another
              entry.
            </p>
            <button type="button" disabled={checking} onClick={() => void reconcile()}>
              {checking ? 'Checking saved games…' : 'Check saved games'}
            </button>
            {draft.checked && (
              <>
                <p>
                  {draft.matches.length
                    ? 'These saved games have the same title:'
                    : 'No saved manual game has this title. Check the library before adding another.'}
                </p>
                {draft.matches.map((game) => (
                  <p key={game.ownershipId}>
                    {game.title}
                    {game.firstReleaseYear ? ` · ${game.firstReleaseYear}` : ''}{' '}
                    <button type="button" onClick={close}>
                      Use saved game
                    </button>
                  </p>
                ))}
                <button
                  type="button"
                  onClick={() => {
                    update({ uncertain: false, checked: false, matches: [] })
                    command.reset()
                  }}
                >
                  Create another anyway
                </button>
              </>
            )}
            <Notice error={checkError} />
          </section>
        )}
        <div className="form-actions">
          <button
            className="primary-button"
            disabled={
              command.isPending ||
              draft.sending ||
              draft.uncertain ||
              Boolean(current) ||
              (command.error instanceof ApiError && command.error.conflict)
            }
          >
            Save game
          </button>
          <button type="button" disabled={draft.sending} onClick={close}>
            Cancel
          </button>
        </div>
        {draft.sending && <p role="status">Saving your game…</p>}
      </form>
    </section>
  )
}

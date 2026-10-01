import { useQueryClient } from '@tanstack/react-query'
import { ApiError, request } from '../api/client'
import { useApiQuery } from '../api/hooks'
import type { IdentityReview } from '../api/types'
import { useViewState } from '../viewState'
import { Empty, Notice } from './shared'
import { IgdbCandidateRow, IgdbCover, type IgdbCandidate } from './igdb-candidate'
export interface IgdbState {
  workId: number
  mappingRevision: number
  pin?: { igdbId: number } | null
  revision: string
  available?: boolean
}
interface Holder {
  workId: number
  title: string
  coverUrl?: string | null
  firstReleaseYear?: number | null
}
const assignmentMessages: Record<string, string> = {
  WorkNotFound: 'That game is no longer in your library.',
  MetadataUnavailable: 'IGDB has no metadata for that entry. Nothing changed.',
  IgdbIdClaimedByAnotherWork: 'Another game in your library already uses that IGDB entry.',
  MappingChanged:
    "This game's match changed while the metadata loaded. Reopen the match picker to choose again.",
  IdentifierHistoryUnavailable:
    'The old ID has no recorded origin. Keep this entry, or add the corrected game separately in Library settings.',
  StorefrontObservation:
    'A store entry uses the old ID. Keep this entry, or add the corrected game separately in Library settings.',
  Failed: "Couldn't save that. Nothing changed.",
}

export function IgdbMatch({
  workId,
  title,
  onChanged,
}: {
  workId: number
  title: string
  onChanged?: (note: string) => void
}) {
  const state = useApiQuery<IgdbState>('metadata.igdb', { workId })
  const [draft, setDraft] = useViewState(`draft:igdb:${workId}`, {
    query: title,
    results: [] as IgdbCandidate[],
    searched: false,
    idMissing: false,
    idMatch: null as number | null,
    revision: '',
    searchId: null as string | null,
  })
  const searching = Boolean(draft.searchId)
  const [error, setError] = useViewState<unknown>(`igdb:${workId}:error`, null)
  const [message, setMessage] = useViewState(`igdb:${workId}:message`, '')
  const [holder, setHolder] = useViewState<Holder | null>(`igdb:${workId}:holder`, null)
  const [offerRevision, setOfferRevision] = useViewState(`igdb:${workId}:offer-revision`, '')
  const [sending, setSending] = useViewState(`igdb:${workId}:sending`, false)
  const [operation, setOperation] = useViewState(`igdb:${workId}:operation`, 'Saving…')
  const client = useQueryClient()
  async function changed(note: string) {
    const saved = await state.refetch()
    if (saved.data) setDraft((previous) => ({ ...previous, revision: saved.data!.revision }))
    await Promise.all([
      client.invalidateQueries({
        queryKey: ['api'],
        predicate: (query) => query.queryKey[1] !== 'metadata.igdb',
      }),
      client.invalidateQueries({ queryKey: ['artwork'] }),
      client.invalidateQueries({ queryKey: ['artwork-image'] }),
    ])
    setMessage(note)
    onChanged?.(note)
  }
  function failureResult(value: unknown) {
    setError(value)
    if (value instanceof ApiError && value.uncertain) void client.invalidateQueries({ queryKey: ['api'] })
  }
  async function search() {
    if (!draft.query.trim() || searching || sending) return
    const searchId = crypto.randomUUID()
    setDraft((previous) => ({ ...previous, searchId }))
    setError(null)
    setHolder(null)
    setMessage('')
    try {
      const query = draft.query.trim()
      const numeric = /^\d+$/.test(query) && Number(query) > 0 && Number.isSafeInteger(Number(query))
      const [results, candidate] = await Promise.all([
        request<IgdbCandidate[]>('metadata.search', { title: query }),
        numeric
          ? request<IgdbCandidate>('metadata.candidate', { igdbId: Number(query) }).catch((failure) => {
              if (failure instanceof ApiError && failure.status === 404) return null
              throw failure
            })
          : Promise.resolve(null),
      ])
      setDraft((previous) =>
        previous.searchId === searchId
          ? {
              ...previous,
              results: candidate
                ? [candidate, ...results.filter((item) => item.igdbId !== candidate.igdbId)]
                : results,
              searched: true,
              idMissing: numeric && !candidate,
              idMatch: candidate?.igdbId ?? null,
              revision: state.data?.revision ?? '',
            }
          : previous,
      )
    } catch (failure) {
      setError(failure)
    } finally {
      setDraft((previous) => (previous.searchId === searchId ? { ...previous, searchId: null } : previous))
    }
  }
  async function assign(candidate: IgdbCandidate) {
    if (sending || searching || !draft.revision) return
    setSending(true)
    setOperation('Saving…')
    setHolder(null)
    setError(null)
    setMessage('')
    try {
      const result = await request<{ outcome: string }>(
        'metadata.assign',
        { workId },
        {
          igdbId: candidate.igdbId,
          expectedRevision: draft.revision,
        },
      )
      if (result.outcome === 'Assigned') {
        await changed(`Now using ${candidate.name}.`)
        return
      }
      setError(new Error(assignmentMessages[result.outcome] ?? assignmentMessages.Failed))
      if (result.outcome === 'IgdbIdClaimedByAnotherWork') {
        const claim = await request<Holder>('metadata.claiming', { igdbId: candidate.igdbId }).catch(
          (failure) => {
            if (failure instanceof ApiError && failure.status === 404) return null
            throw failure
          },
        )
        if (!claim || claim.workId === workId) return
        const review = await request<IdentityReview>('identity.get').catch((failure) => {
          if (failure instanceof ApiError && [404, 501].includes(failure.status)) return null
          throw failure
        })
        if (review?.revision) {
          setHolder(claim)
          setOfferRevision(review.revision)
          setError(null)
        }
      }
    } catch (failure) {
      failureResult(failure)
    } finally {
      setSending(false)
    }
  }
  async function group() {
    if (!holder || sending) return
    setSending(true)
    setOperation('Linking…')
    setError(null)
    setMessage('')
    try {
      await request('identity.link', undefined, {
        expectedRevision: offerRevision,
        parentWorkId: holder.workId,
        childWorkIds: [workId],
        kind: 'same_game',
        relationLabel: null,
        rejectedCandidateIds: [],
        refusedPairs: [],
      })
      setHolder(null)
      await changed(`Linked with ${holder.title}.`)
    } catch (failure) {
      const refused =
        failure instanceof ApiError &&
        failure.current !== null &&
        typeof failure.current === 'object' &&
        'refusal' in failure.current
      failureResult(
        refused
          ? new ApiError(failure.status, "Couldn't link those. Nothing changed.", failure.current)
          : failure instanceof ApiError && (failure.conflict || failure.uncertain)
            ? failure
            : new Error("Couldn't link those. Nothing changed."),
      )
    } finally {
      setSending(false)
    }
  }
  const conflict = error instanceof ApiError && error.conflict
  if (state.data?.available === false) return null
  return (
    <section className="feature-panel">
      <h2>Wrong game?</h2>
      <p>
        Find the right IGDB record. Choosing it replaces this game’s metadata and keeps that match for future
        refreshes.
      </p>
      <form
        className="editor-form"
        onSubmit={(e) => {
          e.preventDefault()
          void search()
        }}
      >
        <label className="field">
          Game title or IGDB ID
          <input
            autoFocus
            value={draft.query}
            disabled={searching || sending}
            onChange={(e) => setDraft({ ...draft, query: e.target.value })}
          />
        </label>
        <button disabled={searching || sending || !draft.query.trim() || !state.data}>
          {searching ? 'Searching IGDB…' : 'Search IGDB'}
        </button>
      </form>
      {sending && <p role="status">{operation}</p>}
      <Notice error={state.error || error} message={message} />
      {conflict && (
        <div className="conflict-panel">
          <p>Your choices are preserved. Refresh the saved match before choosing again.</p>
          <button
            onClick={async () => {
              const current = await state.refetch()
              if (current.data) {
                setDraft({ ...draft, revision: current.data.revision })
                setError(null)
                setHolder(null)
              }
            }}
          >
            Refresh saved match
          </button>
        </div>
      )}
      {draft.idMissing && !sending && !searching && !error && !holder && (
        <p className="muted">No IGDB entry uses that ID. Title matches are shown below.</p>
      )}
      {draft.searched && !draft.results.length && !sending && !searching && !error && !holder && (
        <Empty>No matching games. Try a different title or an IGDB ID.</Empty>
      )}
      <div className="igdb-candidates">
        {draft.results.map((candidate) => (
          <IgdbCandidateRow
            candidate={candidate}
            idMatch={draft.idMatch === candidate.igdbId}
            key={candidate.igdbId}
          >
            <button
              title={`Use ${candidate.name}`}
              disabled={sending || searching || conflict || !draft.revision}
              onClick={() => void assign(candidate)}
            >
              Use this match
            </button>
          </IgdbCandidateRow>
        ))}
      </div>
      {holder && (
        <div className="conflict-panel">
          <IgdbCover url={holder.coverUrl} />
          <h3>Is this the same game as {holder.title}?</h3>
          {holder.firstReleaseYear && <p>{holder.firstReleaseYear}</p>}
          <p>This game already holds the chosen match. Group their editions to keep one library identity.</p>
          <div className="form-actions">
            <button
              title={`Link as the same game as ${holder.title}`}
              disabled={sending || conflict}
              onClick={() => void group()}
            >
              Yes, group these editions
            </button>
            <button
              disabled={sending}
              onClick={() => {
                setHolder(null)
                setError(new Error(assignmentMessages.IgdbIdClaimedByAnotherWork))
              }}
            >
              Keep them separate
            </button>
          </div>
        </div>
      )}
      {state.data?.pin && (
        <div className="form-actions">
          <p>Matched to IGDB {state.data.pin.igdbId}.</p>
          <button
            disabled={sending || searching}
            onClick={async () => {
              if (sending || searching) return
              setSending(true)
              setOperation('Saving…')
              setError(null)
              setMessage('')
              try {
                const cleared = await request<boolean>(
                  'metadata.clear',
                  { workId },
                  {
                    expectedRevision: state.data!.revision,
                  },
                )
                if (cleared) await changed('Returned to automatic metadata matching.')
                else setError(new Error("Couldn't clear that. Nothing changed."))
              } catch (failure) {
                failureResult(failure)
              } finally {
                setSending(false)
              }
            }}
          >
            Return to automatic matching
          </button>
        </div>
      )}
    </section>
  )
}

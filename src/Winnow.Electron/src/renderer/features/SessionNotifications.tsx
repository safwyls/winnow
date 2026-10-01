import { useCallback, useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import type { BackendEvent } from '../../shared/bridge'
import { request } from '../api/client'
import { X } from 'lucide-react'
import { useApiQuery, useLibrary } from '../api/hooks'
import type { JournalResponse, Mode } from '../api/types'
import { clearViewState, useViewState } from '../viewState'
import { JournalDraft } from './Journal'
import { Notice } from './shared'
import { RefreshQueue } from '../refresh'
import './session-notifications.css'
import { sessionDuration } from './journal-prompt-controls'
import { controllerScope } from '../controller'

interface SessionPrompt {
  sessionId: number
  ownershipId: number
  durationSeconds: number
  title: string
}
interface PromptDraft {
  note: string
  rating: number
  sending: boolean
}
interface SessionDiagnostics {
  sessionFailures: unknown[]
}

/** Live prompts are for newly finished sittings, never reconstructed from playtime totals. */
export function SessionNotifications({
  mode,
  suspended = false,
  editText,
}: {
  mode: Mode
  suspended?: boolean
  editText?(input: HTMLInputElement | HTMLTextAreaElement): void
}) {
  const library = useLibrary()
  const diagnostics = useApiQuery<SessionDiagnostics>('diagnostics.get')
  const client = useQueryClient()
  const [prompt, setPrompt] = useState<SessionPrompt | null>(null)
  const [showCard, setShowCard] = useState(false)
  const [folderError, setFolderError] = useState<unknown>(null)
  const [draft] = useViewState<PromptDraft | null>(`draft:journal:${prompt?.sessionId ?? 'none'}`, null)
  const touched = Boolean(draft?.sending || draft?.rating || draft?.note.trim())
  const latest = useRef({ library: library.data, touched, prompt })
  latest.current = { library: library.data, touched, prompt }
  const offered = useRef(new Set<number>())
  const activeOffer = useRef<{ sessionId: number; activated: boolean } | null>(null)
  const dismiss = useCallback(() => {
    if (latest.current.prompt) {
      clearViewState(`draft:journal:${latest.current.prompt.sessionId}`)
      void window.winnow.clearJournalNotification?.(latest.current.prompt.sessionId).catch(() => {})
    }
    activeOffer.current = null
    setShowCard(false)
    setPrompt(null)
  }, [])

  useEffect(() => {
    let disposed = false
    let version = 0
    const diagnosticsRefresh = new RefreshQueue(async () => {
      const reads = client
        .getQueryCache()
        .findAll({ queryKey: ['api', 'diagnostics.get'], fetchStatus: 'fetching' })
      await Promise.allSettled(reads.map((query) => query.promise))
      await client.invalidateQueries({ queryKey: ['api', 'diagnostics.get'] })
    })
    async function receive(event: BackendEvent) {
      if (event.kind === 'diagnostics.changed' || event.kind === 'resync-required') {
        diagnosticsRefresh.request()
        return
      }
      if (event.kind !== 'session.ended') return
      const match = /^sessions\/([1-9]\d*)$/.exec(event.resource ?? '')
      const sessionId = match ? Number(match[1]) : NaN
      if (!Number.isSafeInteger(sessionId) || offered.current.has(sessionId)) return
      offered.current.add(sessionId)
      // A running library can collect many sessions; retain a bounded replay guard.
      if (offered.current.size > 4096) offered.current.delete(offered.current.values().next().value!)
      // A sitting received during editing is discarded, even if that draft closes during later reads.
      if (latest.current.touched) return
      const generation = ++version
      try {
        const preferences = await request<{ promptAfterPlay: boolean }>('journal.preferences.get')
        if (!preferences.promptAfterPlay || disposed || generation !== version) return
        const ended = await request<Omit<SessionPrompt, 'title'>>('journal.prompt', { sessionId })
        if (disposed || generation !== version || latest.current.touched || ended.sessionId !== sessionId)
          return
        const game = latest.current.library?.games.find((game) =>
          game.entries.some((entry) => entry.ownershipId === ended.ownershipId),
        )
        // Visibility choices also govern notification titles.
        if (!game) return
        if (activeOffer.current)
          void window.winnow.clearJournalNotification?.(activeOffer.current.sessionId).catch(() => {})
        const offer = { sessionId, activated: false }
        activeOffer.current = offer
        setPrompt({ ...ended, title: game.title })
        setShowCard(false)
        let submitted = false
        try {
          submitted = (await window.winnow.notifySessionEnded?.({ sessionId, title: game.title })) ?? false
        } catch {
          /* An unavailable notification falls back to the same in-window editor. */
        }
        if (!disposed && activeOffer.current === offer) setShowCard(!submitted || offer.activated)
      } catch {
        // A deleted or unavailable session must not open a note for a different sitting.
      }
    }
    const unsubscribe = window.winnow.onEvent?.((event) => void receive(event))
    const unsubscribeActivation = window.winnow.onJournalNotificationActivated?.((sessionId) => {
      if (activeOffer.current?.sessionId !== sessionId) return
      activeOffer.current.activated = true
      setShowCard(true)
    })
    return () => {
      disposed = true
      diagnosticsRefresh.dispose()
      unsubscribe?.()
      unsubscribeActivation?.()
      if (activeOffer.current)
        void window.winnow.clearJournalNotification?.(activeOffer.current.sessionId).catch(() => {})
      activeOffer.current = null
    }
  }, [client])

  useEffect(() => {
    if (!prompt || touched || suspended) return
    const timer = setTimeout(dismiss, 120_000)
    return () => clearTimeout(timer)
  }, [prompt, touched, suspended, dismiss])

  async function openLogs() {
    setFolderError(null)
    try {
      await window.winnow.openDataFolder?.('logs')
    } catch (failure) {
      setFolderError(failure)
    }
  }
  if (suspended) return null
  return (
    <div className={`session-notifications mode-${mode}`}>
      {!!diagnostics.data?.sessionFailures?.length && (
        <aside className="session-health-notice" role="status">
          <p>
            Session tracking needs attention. Some play sessions may be missing. Winnow will retry
            automatically.
          </p>
          {window.winnow.openDataFolder && <button onClick={() => void openLogs()}>Open logs folder</button>}
          <Notice error={folderError} />
        </aside>
      )}
      {prompt && showCard && (
        <SessionPromptCard
          prompt={prompt}
          dismiss={dismiss}
          sending={!!draft?.sending}
          mode={mode}
          editText={editText}
        />
      )}
    </div>
  )
}

function SessionPromptCard({
  prompt,
  dismiss,
  sending,
  mode,
  editText,
}: {
  prompt: SessionPrompt
  dismiss: () => void
  sending: boolean
  mode: Mode
  editText?(input: HTMLInputElement | HTMLTextAreaElement): void
}) {
  const query = useApiQuery<JournalResponse>('journal.get', { sessionId: prompt.sessionId })
  const card = useRef<HTMLElement>(null)
  useEffect(() => {
    const back = (event: KeyboardEvent) => {
      if (event.key === 'Tab' && mode === 'fullscreen' && controllerScope() === card.current) {
        const controls = [
          ...card.current!.querySelectorAll<HTMLElement>(
            'button:not(:disabled), textarea:not(:disabled), input:not(:disabled):not([type="hidden"])',
          ),
        ]
        const current = controls.indexOf(document.activeElement as HTMLElement)
        if (current < 0 || (event.shiftKey ? current === 0 : current === controls.length - 1)) {
          event.preventDefault()
          const target =
            current < 0 && !event.shiftKey
              ? (card.current?.querySelector<HTMLElement>('[data-controller-initial]:not(:disabled)') ??
                controls[0])
              : controls[event.shiftKey ? controls.length - 1 : 0]
          target?.focus()
        }
        return
      }
      if (event.key !== 'Escape') return
      const scope = controllerScope()
      if (mode === 'fullscreen' ? scope !== card.current : !card.current?.contains(document.activeElement))
        return
      event.preventDefault()
      event.stopPropagation()
      if (!sending) dismiss()
    }
    window.addEventListener('keydown', back, true)
    return () => window.removeEventListener('keydown', back, true)
  }, [mode, sending, dismiss])
  return (
    <aside
      ref={card}
      className="session-prompt feature-panel"
      role={mode === 'fullscreen' ? 'dialog' : undefined}
      aria-label={`Journal after playing ${prompt.title}`}
      key={prompt.sessionId}
      onKeyDown={(event) => {
        if (
          mode !== 'fullscreen' ||
          event.defaultPrevented ||
          !['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)
        )
          return
        const target = event.target as HTMLElement
        if (target.matches('input, textarea, select')) return
        const rows = ['.journal-edit-note', '.journal-rating button', 'form > .form-actions button'].map(
          (selector) =>
            [...event.currentTarget.querySelectorAll<HTMLButtonElement>(selector)].filter(
              (button) => !button.disabled,
            ),
        )
        const row = rows.findIndex((controls) => controls.includes(target as HTMLButtonElement))
        if (row < 0) return
        event.preventDefault()
        event.stopPropagation()
        if (sending) return
        const column = rows[row]!.indexOf(target as HTMLButtonElement)
        const vertical = event.key === 'ArrowUp' || event.key === 'ArrowDown'
        const delta = event.key === 'ArrowUp' || event.key === 'ArrowLeft' ? -1 : 1
        const nextRow = vertical ? Math.max(0, Math.min(rows.length - 1, row + delta)) : row
        const nextColumn = Math.max(0, Math.min(rows[nextRow]!.length - 1, column + (vertical ? 0 : delta)))
        const next = rows[nextRow]?.[nextColumn]
        next?.focus({ preventScroll: true })
        next?.scrollIntoView?.({ block: 'nearest', inline: 'nearest', behavior: 'instant' })
      }}
    >
      <header
        className={`feature-heading${mode === 'fullscreen' ? ' journal-prompt-group journal-session-group' : ''}`}
      >
        <div>
          {mode === 'fullscreen' ? (
            <>
              <h2 className="journal-group-title">YOUR LAST SESSION</h2>
              <h3 className="journal-game-title">{prompt.title}</h3>
            </>
          ) : (
            <h2>{prompt.title}</h2>
          )}
          <p className="journal-duration">{sessionDuration(prompt.durationSeconds)}</p>
        </div>
        <button disabled={sending} onClick={dismiss} aria-label="Dismiss journal prompt">
          <X size={16} aria-hidden="true" />
        </button>
      </header>
      {query.isPending && <p role="status">Loading your note…</p>}
      <Notice error={query.error} />
      {query.data && (
        <div
          onSubmitCapture={(event) => {
            const form = event.target as HTMLFormElement
            const data = new FormData(form)
            const note = String(data.get('note') ?? '').trim()
            const rating = Number(data.get('rating') ?? 0)
            if (!note && !rating && !query.data?.note && !query.data?.rating) {
              event.preventDefault()
              event.stopPropagation()
              dismiss()
            }
          }}
        >
          <JournalDraft initial={query.data} onClose={dismiss} promptMode={mode} editText={editText} />
        </div>
      )}
      {mode === 'fullscreen' && (
        <p className="journal-controller-hints">
          {sending ? 'Saving…' : 'A Select · Y Keyboard · B Dismiss'}
        </p>
      )}
    </aside>
  )
}

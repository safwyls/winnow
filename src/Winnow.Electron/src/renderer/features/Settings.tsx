import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Check, RefreshCw } from 'lucide-react'
import * as Dialog from '@radix-ui/react-dialog'
import { createClientId, dateLabel, request, openExternal } from '../api/client'
import { useApiQuery, useCommand, useLibrary } from '../api/hooks'
import type { BackendOperation, FeedVerdict, LibraryPreferences, Mode, StoreConnections } from '../api/types'
import { Empty, Notice } from './shared'
import { readEpicCallback, type EpicChallenge } from '../api/auth'
import { useViewState } from '../viewState'
import {
  AccountVisibility,
  ApplicationPreferences,
  ArtworkSourcePreferences,
  FullscreenPreferences,
  LibraryPresentationPreferences,
} from './SettingsPreferences'
import { useSetupBusy, useSetupPreferenceError } from './settingsState'
import { AccountStatistics, SteamPageImport } from './Accounts'
import { SteamCapture, SteamCaptureReview } from './SteamCapture'
import type { SteamCaptureResult } from '../../shared/bridge'
import { PluginSettings } from './PluginSettings'
export { PluginCard } from './PluginSettings'
import { SteamConnectionPanel } from './SteamConnectionPanel'
import { steamCapturePermissionExplanation, steamConnectionState } from './steamConnection'
import { NativeEpicAccount } from './EpicAccount'
import { SteamAccountOperation, useSteamAccountBusy } from './SteamAccountOperation'
import { Platforms } from './Platforms'
import { RatingCapPreference, useLibraryPreferenceChange } from './RatingCap'
import { IgdbConnectionPanel } from './IgdbSettings'
export { IgdbForm } from './IgdbSettings'
import { useSteamModal } from './SteamModals'
import { FullscreenAppearance } from './FullscreenAppearance'
import { ControllerGuide } from './ControllerGuide'
import { FullscreenSettingsAction } from './FullscreenSettingRows'

export function Settings({
  mode = 'desktop',
  ratingCapInDisplayPreferences = false,
  fullscreenThemeControls,
}: {
  mode?: Mode
  ratingCapInDisplayPreferences?: boolean
  fullscreenThemeControls?: ReactNode
}) {
  const [savedTab, setTab] = useViewState(
    `${mode}:settings:tab`,
    mode === 'fullscreen' ? 'Appearance' : 'Platforms',
  )
  const tab = savedTab === 'Connections' ? 'Platforms' : savedTab === 'Providers' ? 'Plugins' : savedTab
  const parentTab =
    tab === 'Spending' || tab === 'Recommendations' ? 'Library' : tab === 'Operations' ? 'Application' : tab
  const page = useRef<HTMLElement>(null)
  const previousTab = useRef(tab)
  useEffect(() => {
    const previous = previousTab.current
    previousTab.current = tab
    if (mode !== 'fullscreen' || previous === tab) return
    const timer = setTimeout(() => {
      if (page.current?.closest('[inert]')) return
      const target =
        parentTab !== tab
          ? page.current?.querySelector<HTMLButtonElement>('[data-settings-child-back]')
          : [
              ...(page.current?.querySelectorAll<HTMLButtonElement>('.fullscreen-settings-content button') ??
                []),
            ].find((button) => button.getAttribute('aria-label') === previous)
      target?.focus()
    }, 0)
    return () => clearTimeout(timer)
  }, [mode, tab, parentTab])
  const sections =
    mode === 'fullscreen'
      ? ['Appearance', 'Controller', 'Library', 'Platforms', 'Metadata & artwork', 'Plugins', 'Application']
      : [
          'Platforms',
          'Metadata & artwork',
          'Plugins',
          'Application',
          'Library',
          'Appearance',
          'Spending',
          'Recommendations',
          'Operations',
        ]
  const stores = useApiQuery<StoreConnections>('connections.get')
  const preferences = useApiQuery<LibraryPreferences>('preferences.library.get')
  const operations = useApiQuery<BackendOperation[]>('operations.get')
  const command = useCommand()
  return (
    <section
      ref={page}
      className={`feature-page settings-page mode-${mode}${mode === 'fullscreen' && tab === 'Controller' ? ' fullscreen-controller-page' : ''}`}
      onKeyDown={(event) => {
        // A portal's Escape belongs to its own layer even though React bubbles through this page.
        if (
          event.defaultPrevented ||
          !event.currentTarget.contains(event.target as Node) ||
          (event.target as Element).closest('[role="dialog"], [role="alertdialog"], [role="menu"]')
        )
          return
        if (mode === 'fullscreen' && parentTab !== tab && event.key === 'Escape') {
          event.preventDefault()
          event.stopPropagation()
          setTab(parentTab)
        }
      }}
    >
      <header className="feature-heading">
        <div>
          {mode === 'fullscreen' ? (
            <h1>Make yourself comfortable</h1>
          ) : (
            <>
              <p className="eyebrow">A PLACE FOR EVERYTHING</p>
              <h1>Make yourself at home.</h1>
              <p>Your library stays on this computer. Connections enrich what is already yours.</p>
            </>
          )}
        </div>
      </header>
      <nav className="tabs" aria-label="Settings section">
        {sections.map((name) => (
          <button
            key={name}
            data-controller-tab
            aria-pressed={(mode === 'fullscreen' ? parentTab : tab) === name}
            onClick={(event) => {
              setTab(name)
              if (mode === 'fullscreen' && name === 'Controller')
                event.currentTarget.focus({ preventScroll: true })
            }}
            onFocus={(event) => event.currentTarget.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })}
          >
            {name}
          </button>
        ))}
      </nav>
      {mode === 'fullscreen' && parentTab !== tab && (
        <button data-settings-child-back onClick={() => setTab(parentTab)}>
          Back to {parentTab}
        </button>
      )}
      {mode === 'fullscreen' && tab === 'Controller' && <ControllerGuide />}
      {tab === 'Platforms' && (
        <div className="feature-grid">
          {stores.data ? (
            <Platforms
              snapshot={stores.data}
              steam={(titleCount) => (
                <SteamConnectionCard
                  snapshot={stores.data}
                  mode={mode}
                  error={stores.error}
                  titleCount={titleCount}
                />
              )}
              epic={<EpicConnectionCard snapshot={stores.data} mode={mode} />}
            />
          ) : (
            <Notice error={stores.error} message="Loading platform connections…" />
          )}
          <Notice error={command.error} />
        </div>
      )}
      {tab === 'Metadata & artwork' && (
        <>
          <section className="feature-panel">
            <h2>IGDB</h2>
            <p>Descriptions, game identity, and artwork from IGDB.</p>
            <IgdbConnectionPanel mode={mode} />
          </section>
          <ArtworkSourcePreferences />
        </>
      )}
      {tab === 'Plugins' && <PluginSettings mode={mode} />}
      {tab === 'Library' && (
        <>
          <section className="feature-panel">
            <h2>Library visibility</h2>
            <Notice error={preferences.error} />
            {preferences.data && (
              <LibraryPreferenceForm initial={preferences.data} key={JSON.stringify(preferences.data)} />
            )}
            {!ratingCapInDisplayPreferences && <RatingCapPreference mode={mode} />}
            <AccountVisibility />
          </section>
          <LibraryPresentationPreferences />
          {mode === 'fullscreen' && (
            <section className="fullscreen-settings-content" aria-label="Library tools">
              <FullscreenSettingsAction label="Spending" onClick={() => setTab('Spending')} />
              <FullscreenSettingsAction label="Recommendations" onClick={() => setTab('Recommendations')} />
            </section>
          )}
        </>
      )}
      {tab === 'Application' && (
        <>
          <ApplicationPreferences />
          {mode === 'fullscreen' && (
            <section className="fullscreen-settings-content">
              <FullscreenSettingsAction label="Operations" onClick={() => setTab('Operations')} />
            </section>
          )}
        </>
      )}
      {tab === 'Spending' && <AccountStatistics mode={mode} />}
      {tab === 'Appearance' && (
        <>
          {mode === 'fullscreen' ? (
            <FullscreenAppearance themeControls={fullscreenThemeControls} />
          ) : (
            <FullscreenPreferences mode={mode} />
          )}
        </>
      )}
      {tab === 'Recommendations' && <FeedbackHistory />}
      {tab === 'Operations' && (
        <section className="feature-panel">
          <div className="feature-heading">
            <div>
              <h2>Background work</h2>
              <p>These operations continue while you browse.</p>
            </div>
            <button
              disabled={command.isPending}
              onClick={() =>
                command.mutate({ route: 'operations.sync', body: { operationId: createClientId() } })
              }
            >
              <RefreshCw size={16} /> Refresh library metadata
            </button>
          </div>
          <Notice error={operations.error || command.error} />
          {operations.data?.length ? (
            operations.data.map((operation) => (
              <article className="operation-row" key={operation.id}>
                <div>
                  <h3>{operation.kind.replaceAll('-', ' ')}</h3>
                  <p>{operation.message}</p>
                  <small>{operation.state}</small>
                </div>
                {['running', 'queued', 'pending'].includes(operation.state.toLowerCase()) && (
                  <button
                    onClick={() =>
                      command.mutate({ route: 'operations.cancel', params: { id: operation.id } })
                    }
                  >
                    Cancel
                  </button>
                )}
              </article>
            ))
          ) : (
            <Empty>No background operations yet.</Empty>
          )}
        </section>
      )}
    </section>
  )
}

export function FeedbackHistory() {
  const history = useApiQuery<FeedVerdict[]>('feed.history')
  const library = useLibrary()
  const command = useCommand<boolean>()
  const [message, setMessage] = useState('')
  async function undo(verdict: FeedVerdict) {
    setMessage('')
    try {
      const changed = await command.mutateAsync({
        route: 'feed.revoke',
        body: { releaseId: verdict.releaseId, kind: verdict.kind },
      })
      setMessage(
        changed
          ? 'Your feedback was undone. This game can appear in recommendations again.'
          : 'This feedback is no longer active.',
      )
    } catch {
      /* Preserve the history until the backend confirms the command. */
    }
  }
  return (
    <section className="feature-panel">
      <h2>What you told Winnow</h2>
      <p>Your choices shape future recommendations. Change your mind whenever you like.</p>
      <Notice error={history.error || library.error || command.error} message={message} />
      {history.isPending ? (
        <p role="status">Loading your feedback…</p>
      ) : history.data?.length ? (
        <div className="timeline">
          {history.data.map((verdict, index) => {
            const title =
              library.data?.games.find((game) =>
                game.entries.some((entry) => entry.releaseId === verdict.releaseId),
              )?.title ?? 'Game no longer visible in your library'
            const label = verdict.kind === 0 ? 'Not interested' : verdict.kind === 1 ? 'Not now' : 'Feedback'
            return (
              <article
                className="timeline-entry"
                key={`${verdict.releaseId}:${verdict.kind}:${verdict.createdAt}:${index}`}
              >
                <time dateTime={verdict.createdAt}>{dateLabel(verdict.createdAt)}</time>
                <div>
                  <h3>{title}</h3>
                  <p>
                    {label} ·{' '}
                    {verdict.status === 0
                      ? 'Active'
                      : verdict.status === 1
                        ? 'Undone'
                        : verdict.status === 2
                          ? 'Expired'
                          : 'Unknown status'}
                  </p>
                  {verdict.status === 0 && verdict.expiresAt && (
                    <small>Until {dateLabel(verdict.expiresAt)}</small>
                  )}
                </div>
                {verdict.status === 0 && (verdict.kind === 0 || verdict.kind === 1) && (
                  <button
                    disabled={command.isPending}
                    aria-label={`Undo ${label} for ${title}`}
                    onClick={() => void undo(verdict)}
                  >
                    Undo
                  </button>
                )}
              </article>
            )
          })}
        </div>
      ) : (
        <Empty>You have not dismissed or snoozed any recommendations.</Empty>
      )}
    </section>
  )
}

export function SteamConnectionCard({
  snapshot,
  mode = 'desktop',
  purchase = true,
  error,
  titleCount,
}: {
  snapshot: StoreConnections
  mode?: Mode
  purchase?: boolean
  error?: unknown
  titleCount?: number
}) {
  const command = useCommand()
  const state = steamConnectionState(snapshot)
  useSetupBusy(command.isPending)
  return (
    <SteamAccountOperation>
      <SteamConnectionPanel
        snapshot={snapshot}
        busy={command.isPending}
        error={error || command.error}
        titleCount={titleCount}
        signIn={
          <SteamAccount
            key={mode}
            label={state.signInLabel}
            showAction={state.showSignIn}
            sessionPresent={snapshot.steam.hasSession}
          />
        }
        keyEditor={<SteamKeyForm key={mode} hasKey={snapshot.steam.hasApiKey} />}
        purchase={
          purchase ? (
            <>
              <SteamPageImport />
              <SteamCapture />
            </>
          ) : null
        }
        onSignOut={() => command.mutate({ route: 'connections.steam.signOut' })}
        onClearKey={() => command.mutate({ route: 'connections.steam.key', body: { key: null } })}
      />
    </SteamAccountOperation>
  )
}

export function EpicConnectionCard({
  snapshot,
  mode = 'desktop',
}: {
  snapshot: StoreConnections
  mode?: Mode
}) {
  const command = useCommand()
  const [epicBusy, setEpicBusy] = useState(false)
  useSetupBusy(command.isPending || epicBusy)
  return (
    <section className={`feature-panel epic-platform-card mode-${mode}`} aria-label="Epic connection">
      <h2>Epic Games</h2>
      <p
        className="connection-state"
        data-tone={snapshot.epic?.isLive ? 'live' : snapshot.epic ? 'attention' : 'quiet'}
      >
        {snapshot.epic?.isLive ? 'SIGNED IN' : snapshot.epic ? 'SESSION EXPIRED' : 'NOT SIGNED IN'}
      </p>
      <p>
        {snapshot.epic?.isLive
          ? `Connected${snapshot.epic.displayName ? ` as ${snapshot.epic.displayName}` : '. Epic did not provide a display name'}.`
          : snapshot.epic
            ? `Epic sign-in expired${snapshot.epic.displayName ? ` for ${snapshot.epic.displayName}` : ''}. Sign in again to reconnect.`
            : 'Installed games are available through the local Epic library.'}
      </p>
      {snapshot.epic && (
        <button
          disabled={command.isPending || epicBusy}
          onClick={() => command.mutate({ route: 'connections.epic.signOut' })}
        >
          Sign out of Epic
        </button>
      )}
      <EpicAccount
        key={mode}
        showAction={!snapshot.epic?.isLive}
        onBusyChange={setEpicBusy}
        label={snapshot.epic ? 'Sign in to Epic again' : 'Connect Epic Games'}
      />
      <p className="muted">Existing account connections are shared with other Winnow frontends.</p>
      <Notice error={command.error} />
    </section>
  )
}

export function SteamKeyForm({ hasKey }: { hasKey?: boolean } = {}) {
  const [key, setKey] = useState('')
  const [message, setMessage] = useState('')
  const [linkError, setLinkError] = useState<unknown>(null)
  const hadKey = useRef(hasKey)
  useEffect(() => {
    if (hadKey.current && hasKey === false) setMessage('Saved API key removed.')
    hadKey.current = hasKey
  }, [hasKey])
  const command = useCommand<number>()
  const busy = useSteamAccountBusy(command.isPending)
  useSetupBusy(command.isPending)
  async function save() {
    if (busy || !key.trim()) return
    setMessage('')
    const result = await command.mutateAsync({ route: 'connections.steam.key', body: { key } })
    if (result === 0) setKey('')
    setMessage(
      result === 0 ? 'API key saved securely.' : 'This computer could not protect the key. It was not saved.',
    )
  }
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        void save().catch(() => {})
      }}
    >
      <label className="field">
        Steam Web API key
        <input
          type="password"
          autoComplete="off"
          value={key}
          onChange={(event) => setKey(event.target.value)}
          required
          maxLength={4096}
        />
      </label>
      <div className="form-actions">
        <button disabled={busy || !key.trim()}>Save API key</button>
        <button
          type="button"
          onClick={() => {
            setLinkError(null)
            void openExternal('https://steamcommunity.com/dev/apikey', { failure: 'inline' }).catch(
              setLinkError,
            )
          }}
        >
          Get a key
        </button>
      </div>
      <Notice error={command.error} message={message} />
      {linkError != null && (
        <>
          <Notice error={linkError} />
          <p>You can open this address in your browser: https://steamcommunity.com/dev/apikey</p>
        </>
      )}
    </form>
  )
}

export function SteamAccount({
  label = 'Sign in to Steam',
  showAction = true,
  sessionPresent,
}: { label?: string; showAction?: boolean; sessionPresent?: boolean } = {}) {
  const [consent, setConsent] = useSteamModal('consent')
  const [staySignedIn, setStaySignedIn] = useState(true)
  const [capturePurchaseHistory, setCapturePurchaseHistory] = useState(false)
  const [capture, setCapture] = useState<SteamCaptureResult | null>(null)
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState<unknown>(null)
  const client = useQueryClient()
  const busy = useSteamAccountBusy(pending)
  const alive = useRef(true)
  const active = useRef(false)
  const hadSession = useRef(sessionPresent)
  useEffect(() => {
    if (hadSession.current && sessionPresent === false) {
      setCapturePurchaseHistory(false)
      setCapture(null)
      setMessage('')
      setError(null)
    }
    hadSession.current = sessionPresent
  }, [sessionPresent])
  useEffect(() => {
    alive.current = true
    return () => {
      alive.current = false
      if (active.current) void window.winnow.cancelSteamWindow?.().catch(() => {})
    }
  }, [])
  useSetupBusy(pending || consent)
  async function signIn() {
    if (!window.winnow.steamSignIn || busy) return
    active.current = true
    setPending(true)
    setError(null)
    setMessage('')
    setCapture(null)
    try {
      const result = await window.winnow.steamSignIn({
        consentGranted: true,
        staySignedIn,
        ...(capturePurchaseHistory ? { capturePurchaseHistory: true } : {}),
      })
      if (!alive.current) return
      setCapture(result.captureDetail || result.pages ? result : null)
      if (!result.signedIn && [1, 3, 5, 6].includes(result.outcome ?? -1))
        throw Error(result.detail || 'Steam sign-in did not succeed. Try again; an API key is unaffected.')
      setMessage(
        result.signedIn
          ? `Steam connected.${result.detail ? ' ' + result.detail : ''}${!result.persisted ? ' This session was not saved to disk; sign in again after a restart.' : ''}${!result.refreshTokenCaptured ? ' Steam did not provide a renewable session. It lasts about a day; tick Remember me on Steam’s own login form to allow renewal.' : ''}${result.accountConfirmed === true ? ' Account confirmed. The account filter is available.' : result.accountConfirmed === false ? ' The account filter is still unavailable. Signing in again should resolve this.' : ''}`
          : result.detail || 'The window closed without signing in. Nothing was stored.',
      )
      setConsent(false)
      // An initial status read may still contain the account state from before this sign-in.
      await client.cancelQueries({
        predicate: (query) =>
          query.queryKey[0] === 'api' &&
          ['connections.get', 'connections.visibility.get'].includes(String(query.queryKey[1])),
      })
      await client.invalidateQueries({ queryKey: ['api'] })
    } catch (failure) {
      if (alive.current) setError(failure)
    } finally {
      active.current = false
      if (alive.current) setPending(false)
    }
  }
  if (!window.winnow.steamSignIn)
    return <p>The sign-in window cannot open in this frontend. A Web API key works without it.</p>
  return (
    <div className="steam-account">
      <Dialog.Root
        open={consent}
        onOpenChange={(open) => {
          if (!pending) {
            setConsent(open)
            if (open) setCapturePurchaseHistory(false)
          }
        }}
      >
        {showAction && (
          <Dialog.Trigger asChild>
            <button disabled={busy}>{label}</button>
          </Dialog.Trigger>
        )}
        <Dialog.Portal>
          <Dialog.Overlay className="setup-overlay consent-overlay" />
          <Dialog.Content
            className="setup-dialog consent-dialog"
            onEscapeKeyDown={(event) => {
              event.stopPropagation()
              if (pending) event.preventDefault()
            }}
          >
            <div className="setup-body">
              <Dialog.Title>Before you sign in</Dialog.Title>
              <Dialog.Description>Connect your account through Steam’s own sign-in page.</Dialog.Description>
              <p>
                Sign in on Steam’s own page in a private window. Winnow reads the account identity and session
                credentials Steam provides after you sign in. Winnow does not read or save your password.
              </p>
              <p>Your session is kept on this computer. Purchase history is a separate, optional import.</p>
              <p className="muted">
                Steam sessions last about a day. Winnow can try to renew them when Steam supplies a refresh
                token. An API key does not expire.
              </p>
              <label className="check-field">
                <input
                  type="checkbox"
                  checked={staySignedIn}
                  disabled={pending}
                  onChange={(event) => setStaySignedIn(event.target.checked)}
                />
                Stay signed in on this computer
              </label>
              <label className="check-field">
                <input
                  type="checkbox"
                  checked={capturePurchaseHistory}
                  disabled={pending}
                  onChange={(event) => setCapturePurchaseHistory(event.target.checked)}
                />
                Also capture purchase history and licences
              </label>
              <p className="muted">{steamCapturePermissionExplanation}</p>
              <div className="form-actions">
                <button disabled={pending} onClick={() => void signIn()}>
                  Continue to Steam
                </button>
                <button
                  disabled={pending && !window.winnow.cancelSteamWindow}
                  onClick={() => {
                    if (pending) void window.winnow.cancelSteamWindow?.().catch(setError)
                    else setConsent(false)
                  }}
                >
                  Cancel sign-in
                </button>
              </div>
              {pending && (
                <p role="status">
                  Sign in in the window that opened; its title reports capture progress. Close that window to
                  stop.
                </p>
              )}
              <Notice error={error} />
            </div>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
      {!consent && <Notice error={error} message={message} />}
      {capture && (
        <SteamCaptureReview
          key={capture.pages?.capturedAt ?? capture.captureDetail}
          capture={capture}
          discard={() => setCapture(null)}
        />
      )}
    </div>
  )
}

export function LibraryPreferenceForm({ initial }: { initial: LibraryPreferences }) {
  const { apply, pending, error } = useLibraryPreferenceChange()
  useSetupBusy(pending)
  useSetupPreferenceError(error)
  return (
    <div className="editor-form">
      <label className="check-field">
        <input
          type="checkbox"
          checked={initial.showNonGameEntries}
          disabled={pending}
          onChange={(event) => apply('showNonGameEntries', event.target.checked)}
        />
        Show tools, demos, and other non-game entries
      </label>
      <label className="check-field">
        <input
          type="checkbox"
          checked={initial.showExplicitContent}
          disabled={pending}
          onChange={(event) => apply('showExplicitContent', event.target.checked)}
        />
        Show explicit content
      </label>
      <p className="muted">
        Unrated games remain visible. These preferences apply to every frontend attached to this library.
      </p>
      <Notice error={error} />
    </div>
  )
}

interface Challenge {
  attemptId: string
  verificationUrl: string
  userCode: string
  expiresAt: string
  pollIntervalSeconds: number
}
export function EpicAccount({
  label = 'Connect Epic Games',
  showAction = true,
  onBusyChange,
}: { label?: string; showAction?: boolean; onBusyChange?: (busy: boolean) => void } = {}) {
  return window.winnow.prepareEpicSignIn &&
    window.winnow.epicSignIn &&
    window.winnow.cancelEpicSignIn &&
    window.winnow.openEpicSignInInBrowser &&
    window.winnow.completeEpicSignIn ? (
    <NativeEpicAccount label={label} showAction={showAction} onBusyChange={onBusyChange} />
  ) : showAction ? (
    <LegacyEpicAccount label={label} onBusyChange={onBusyChange} />
  ) : null
}
function LegacyEpicAccount({
  label,
  onBusyChange,
}: {
  label: string
  onBusyChange?: (busy: boolean) => void
}) {
  const [challenge, setChallenge] = useState<EpicChallenge | null>(null)
  const [accepted, setAccepted] = useState(false)
  const [callback, setCallback] = useState('')
  const [message, setMessage] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [pending, setPending] = useState(false)
  useSetupBusy(pending || !!challenge)
  useEffect(() => {
    onBusyChange?.(pending || !!challenge)
    return () => onBusyChange?.(false)
  }, [pending, challenge, onBusyChange])
  const clientId = useRef(createClientId()).current
  const client = useQueryClient()
  useEffect(() => {
    if (!challenge) return
    return () => {
      void request('connections.cancel', undefined, { clientId, attemptId: challenge.attemptId }).catch(
        () => {},
      )
    }
  }, [challenge, clientId])
  async function begin() {
    setError(null)
    setPending(true)
    try {
      setChallenge(await request<EpicChallenge>('connections.epic.signin', undefined, { clientId }))
      setAccepted(false)
      setCallback('')
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  async function finish() {
    if (!challenge || !accepted) return
    setError(null)
    setPending(true)
    try {
      const completion = readEpicCallback(callback, challenge)
      const result = await request<{ succeeded: boolean; failure: number; persisted: boolean }>(
        'connections.epic.complete',
        undefined,
        { clientId, attemptId: challenge.attemptId, ...completion },
      )
      setCallback('')
      setChallenge(null)
      setMessage(
        result.succeeded
          ? result.persisted
            ? 'Epic Games connected.'
            : 'Epic Games connected for this run.'
          : 'Epic did not accept this sign-in. Start again to get a fresh code.',
      )
      await client.invalidateQueries({ queryKey: ['api'] })
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  return (
    <div
      className="epic-account"
      onKeyDownCapture={(event) => {
        if (event.key === 'Escape' && challenge) {
          event.preventDefault()
          event.stopPropagation()
          if (!pending) {
            setCallback('')
            setChallenge(null)
          }
        }
      }}
    >
      {challenge ? (
        <div className="editor-form">
          <p>{challenge.request.consentNotice}</p>
          <label className="check-field">
            <input
              type="checkbox"
              checked={accepted}
              onChange={(event) => setAccepted(event.target.checked)}
            />
            I agree to connect this account
          </label>
          <button
            disabled={!accepted}
            onClick={() => {
              void openExternal(challenge.request.startUrl)
            }}
          >
            Continue in browser
          </button>
          <p className="muted">
            After signing in, copy the complete final address from your browser and paste it below. It may
            show an unavailable page; the address contains the response.
          </p>
          <label className="field">
            {challenge.request.expectedState ? 'Final sign-in address' : 'Authorization code'}
            <input
              type="password"
              autoComplete="off"
              value={callback}
              onChange={(event) => setCallback(event.target.value)}
            />
          </label>
          <div className="form-actions">
            <button disabled={!accepted || pending || !callback.trim()} onClick={() => void finish()}>
              Finish connecting
            </button>
            <button
              disabled={pending}
              onClick={() => {
                setCallback('')
                setChallenge(null)
              }}
            >
              Cancel
            </button>
          </div>
        </div>
      ) : (
        <button disabled={pending} onClick={() => void begin()}>
          {label}
        </button>
      )}
      <Notice error={error} message={message} />
    </div>
  )
}

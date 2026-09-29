import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Check, RefreshCw } from 'lucide-react'
import * as Dialog from '@radix-ui/react-dialog'
import { createClientId, dateLabel, request, openExternal } from '../api/client'
import { useApiQuery, useCommand, useLibrary } from '../api/hooks'
import type {
  BackendOperation,
  FeedVerdict,
  IgdbConnection,
  LibraryPreferences,
  Mode,
  PluginSnapshot,
  StoreConnections,
} from '../api/types'
import { Empty, Notice } from './shared'
import { readEpicCallback, type EpicChallenge } from '../api/auth'
import { useViewState } from '../viewState'
import {
  AccountVisibility,
  ApplicationPreferences,
  ArtworkSourcePreferences,
  FullscreenPreferences,
  LibraryPresentationPreferences,
  OfficialPluginInstall,
} from './SettingsPreferences'
import { useSetupBusy, useSetupPreferenceError } from './settingsState'
import { AccountStatistics, SteamPageImport } from './Accounts'
import { SteamCapture, SteamCaptureReview } from './SteamCapture'
import type { SteamCaptureResult } from '../../shared/bridge'
import { BackendRestart } from './BackendRestart'
import { SteamConnectionPanel } from './SteamConnectionPanel'
import { steamConnectionState } from './steamConnection'
import { NativeEpicAccount } from './EpicAccount'
import { SteamAccountOperation, useSteamAccountBusy } from './SteamAccountOperation'
import { Platforms } from './Platforms'
import { useSteamModal } from './SteamModals'

export function Settings({ mode = 'desktop' }: { mode?: Mode }) {
  const [savedTab, setTab] = useViewState(`${mode}:settings:tab`, 'Platforms')
  const tab = savedTab === 'Connections' ? 'Platforms' : savedTab
  const stores = useApiQuery<StoreConnections>('connections.get')
  const igdb = useApiQuery<IgdbConnection>('connections.igdb.get')
  const plugins = useApiQuery<PluginSnapshot[]>('plugins.get')
  const preferences = useApiQuery<LibraryPreferences>('preferences.library.get')
  const operations = useApiQuery<BackendOperation[]>('operations.get')
  const command = useCommand()
  return (
    <section className={`feature-page settings-page mode-${mode}`}>
      <header className="feature-heading">
        <div>
          <p className="eyebrow">A PLACE FOR EVERYTHING</p>
          <h1>Make yourself at home.</h1>
          <p>Your library stays on this computer. Connections enrich what is already yours.</p>
        </div>
      </header>
      <nav className="tabs" aria-label="Settings section">
        {[
          'Platforms',
          'Metadata & artwork',
          'Providers',
          'Library',
          'Appearance',
          'Application',
          'Spending',
          'Recommendations',
          'Operations',
        ].map((name) => (
          <button key={name} aria-pressed={tab === name} onClick={() => setTab(name)}>
            {name}
          </button>
        ))}
      </nav>
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
            <Notice error={igdb.error} />
            {igdb.data && <IgdbForm snapshot={igdb.data} key={mode} />}
          </section>
          <ArtworkSourcePreferences />
        </>
      )}
      {tab === 'Providers' && (
        <>
          <p className="muted">
            Provider plugins add library sources and metadata. Frontend themes are managed in the theme
            studio.
          </p>
          <Notice error={plugins.error} />
          <div className="feature-grid">
            {plugins.data?.map((plugin) => (
              <PluginCard key={`${mode}:${plugin.id}`} plugin={plugin} />
            ))}
          </div>
          {plugins.data?.length === 0 && <Empty>No provider plugins are installed.</Empty>}
          <OfficialPluginInstall />
          <BackendRestart />
        </>
      )}
      {tab === 'Library' && (
        <>
          <section className="feature-panel">
            <h2>Library visibility</h2>
            <Notice error={preferences.error} />
            {preferences.data && (
              <LibraryPreferenceForm initial={preferences.data} key={JSON.stringify(preferences.data)} />
            )}
            <AccountVisibility />
          </section>
          <LibraryPresentationPreferences />
        </>
      )}
      {tab === 'Application' && <ApplicationPreferences />}
      {tab === 'Spending' && <AccountStatistics />}
      {tab === 'Appearance' && (
        <>
          <FullscreenPreferences mode={mode} />
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
    <section className="feature-panel" aria-label="Epic connection">
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
    if (busy) return
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
              <p className="muted">
                Off by default. This reads what you bought, what you paid and how licences arrived. You review
                the captured pages before importing.
              </p>
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

export function IgdbForm({ snapshot }: { snapshot: IgdbConnection }) {
  const [clientId, setClientId] = useState(snapshot.clientId)
  const [secret, setSecret] = useState('')
  const [message, setMessage] = useState('')
  const command = useCommand<number | boolean>()
  useSetupBusy(command.isPending)
  async function save() {
    const result = await command.mutateAsync({
      route: 'connections.igdb.put',
      body: { clientId, clientSecret: secret },
    })
    setSecret('')
    setMessage(
      result === 0
        ? 'IGDB credentials saved.'
        : result === 1
          ? 'Enter both a client ID and a client secret.'
          : 'This computer could not protect the credentials. They were not saved.',
    )
  }
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        void save().catch(() => {})
      }}
    >
      <p>
        {snapshot.hasSavedCredentials
          ? 'Saved credentials are available.'
          : snapshot.hasConfigurationCredentials
            ? 'Credentials are supplied by backend configuration.'
            : 'Add a Twitch application to connect IGDB.'}
      </p>
      <label className="field">
        Client ID
        <input
          autoComplete="off"
          value={clientId}
          onChange={(event) => setClientId(event.target.value)}
          required
          maxLength={512}
        />
      </label>
      <label className="field">
        Client secret
        <input
          type="password"
          autoComplete="off"
          value={secret}
          onChange={(event) => setSecret(event.target.value)}
          required
          maxLength={4096}
        />
      </label>
      <div className="form-actions">
        <button disabled={command.isPending}>Save credentials</button>
        <button
          type="button"
          onClick={() => {
            void openExternal('https://dev.twitch.tv/console/apps')
          }}
        >
          Twitch developer console
        </button>
        {snapshot.hasSavedCredentials && (
          <button
            type="button"
            disabled={command.isPending}
            onClick={() => {
              command.mutate({ route: 'connections.igdb.delete' })
              setMessage('')
            }}
          >
            Remove saved credentials
          </button>
        )}
      </div>
      <Notice error={command.error} message={message} />
    </form>
  )
}

export function LibraryPreferenceForm({ initial }: { initial: LibraryPreferences }) {
  const command = useCommand()
  const [reading, setReading] = useState(false)
  const writing = useRef(false)
  useSetupBusy(command.isPending || reading)
  async function change(field: keyof LibraryPreferences, value: boolean | string) {
    // This endpoint replaces the whole object: read the latest fields before changing one.
    const latest = await request<LibraryPreferences>('preferences.library.get')
    await command.mutateAsync({ route: 'preferences.library.put', body: { ...latest, [field]: value } })
  }
  const [readError, setReadError] = useState<unknown>(null)
  useSetupPreferenceError(readError || command.error)
  function apply(field: keyof LibraryPreferences, value: boolean | string) {
    if (writing.current) return
    writing.current = true
    setReading(true)
    setReadError(null)
    void change(field, value)
      .catch(setReadError)
      .finally(() => {
        writing.current = false
        setReading(false)
      })
  }
  return (
    <div className="editor-form">
      <label className="check-field">
        <input
          type="checkbox"
          checked={initial.showNonGameEntries}
          disabled={command.isPending || reading}
          onChange={(event) => apply('showNonGameEntries', event.target.checked)}
        />
        Show tools, demos, and other non-game entries
      </label>
      <label className="check-field">
        <input
          type="checkbox"
          checked={initial.showExplicitContent}
          disabled={command.isPending || reading}
          onChange={(event) => apply('showExplicitContent', event.target.checked)}
        />
        Show explicit content
      </label>
      <label className="field">
        Maturity cap
        <select
          value={initial.maturityCap}
          disabled={command.isPending || reading}
          onChange={(event) => apply('maturityCap', event.target.value)}
        >
          <option value="everyone">Everyone</option>
          <option value="preteen">Preteen</option>
          <option value="teen">Teen</option>
          <option value="mature">Mature</option>
          <option value="restricted18">18 and over</option>
          <option value="adults_only">No rating cap</option>
        </select>
      </label>
      <p className="muted">
        Unrated games remain visible. These preferences apply to every frontend attached to this library.
      </p>
      <Notice error={readError || command.error} />
    </div>
  )
}

export function PluginCard({ plugin }: { plugin: PluginSnapshot }) {
  const [values, setValues] = useState<Record<string, string>>({})
  const [message, setMessage] = useState('')
  const [advanced, setAdvanced] = useState(false)
  const command = useCommand()
  async function save() {
    await command.mutateAsync({
      route: 'plugins.settings',
      params: { pluginId: plugin.id },
      body: {
        values: {
          ...Object.fromEntries(
            plugin.settings
              .filter((setting) => !setting.isSecret)
              .map((setting) => [setting.key, setting.value ?? '']),
          ),
          ...values,
        },
      },
    })
    setValues({})
    setMessage('Provider settings saved.')
  }
  return (
    <section className="feature-panel plugin-card">
      <header className="feature-heading">
        <div>
          <h2>{plugin.name}</h2>
          <small>
            v{plugin.version} · {plugin.capabilities}
          </small>
        </div>
        <label className="check-field">
          <input
            type="checkbox"
            checked={plugin.enabled}
            disabled={command.isPending || !plugin.canConfigure}
            onChange={(event) =>
              command.mutate({
                route: 'plugins.enabled',
                params: { pluginId: plugin.id },
                body: { enabled: event.target.checked },
              })
            }
          />
          Enabled
        </label>
      </header>
      <p>{plugin.description}</p>
      <p className="muted">
        {plugin.status}
        {plugin.restartRequired ? ' · Restart the backend to apply this change.' : ''}
      </p>
      <form
        onSubmit={(event) => {
          event.preventDefault()
          void save().catch(() => {})
        }}
      >
        {plugin.settings.some((setting) => setting.isAdvanced) && (
          <button
            type="button"
            aria-expanded={advanced}
            aria-label={`${advanced ? 'Hide' : 'Show'} advanced settings: ${plugin.name}`}
            onClick={() => setAdvanced(!advanced)}
          >
            {advanced ? 'Hide' : 'Show'} advanced settings
          </button>
        )}
        {plugin.settings
          .filter((setting) => advanced || !setting.isAdvanced)
          .map((setting) => (
            <label className={setting.isBoolean ? 'check-field' : 'field'} key={setting.key}>
              {setting.isBoolean ? (
                <>
                  <input
                    type="checkbox"
                    disabled={!plugin.canConfigure || command.isPending}
                    checked={(values[setting.key] ?? setting.value) === 'true'}
                    onChange={(event) =>
                      setValues({ ...values, [setting.key]: String(event.target.checked) })
                    }
                  />
                  {setting.label}
                </>
              ) : (
                <>
                  {setting.label}
                  <input
                    type={setting.isSecret ? 'password' : 'text'}
                    disabled={!plugin.canConfigure || command.isPending}
                    autoComplete="off"
                    value={values[setting.key] ?? (setting.isSecret ? '' : (setting.value ?? ''))}
                    placeholder={setting.hasStoredSecret ? 'Saved secret — leave blank to keep' : undefined}
                    required={setting.isRequired && !setting.hasStoredSecret}
                    onChange={(event) => {
                      const next = { ...values, [setting.key]: event.target.value }
                      if (setting.isSecret && !event.target.value) delete next[setting.key]
                      setValues(next)
                    }}
                  />
                </>
              )}
              {setting.description && <small>{setting.description}</small>}
              {setting.setupUrl?.startsWith('https://') && (
                <button type="button" onClick={() => void openExternal(setting.setupUrl!)}>
                  Get {setting.label}
                </button>
              )}
              {setting.isSecret && setting.hasStoredSecret && (
                <button
                  type="button"
                  disabled={command.isPending || !plugin.canConfigure}
                  onClick={() => {
                    const next = { ...values }
                    delete next[setting.key]
                    setValues(next)
                    command.mutate({
                      route: 'plugins.removeSecret',
                      params: { pluginId: plugin.id, key: setting.key },
                    })
                  }}
                >
                  Remove saved {setting.label}
                </button>
              )}
            </label>
          ))}
        <div className="form-actions">
          {plugin.canConfigure && plugin.settings.length > 0 && (
            <button disabled={command.isPending} aria-label={`Save ${plugin.name} settings`}>
              Save provider settings
            </button>
          )}
          <button
            type="button"
            disabled={command.isPending || !plugin.enabled || !plugin.isLoaded}
            onClick={() => command.mutate({ route: 'plugins.refresh', params: { pluginId: plugin.id } })}
          >
            Refresh provider
          </button>
        </div>
      </form>
      <Notice error={command.error} message={message} />
      {plugin.hasAccount && <PluginAccount plugin={plugin} />}
    </section>
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

function PluginAccount({ plugin }: { plugin: PluginSnapshot }) {
  const [challenge, setChallenge] = useState<Challenge | null>(null)
  const [message, setMessage] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [pending, setPending] = useState(false)
  const clientId = useRef(createClientId()).current
  const client = useQueryClient()
  useEffect(() => {
    if (!challenge) return
    let stopped = false
    let timer: ReturnType<typeof setTimeout>
    async function poll() {
      if (new Date(challenge!.expiresAt).getTime() <= Date.now()) {
        setMessage('This sign-in expired. Start again to get a new code.')
        setChallenge(null)
        return
      }
      try {
        const result = await request<{ state: number; message: string }>(
          'plugins.poll',
          { pluginId: plugin.id },
          { clientId, attemptId: challenge!.attemptId },
        )
        if (stopped) return
        setMessage(result.message)
        if (result.state >= 2) {
          setChallenge(null)
          void client.invalidateQueries({ queryKey: ['api'] })
          return
        }
        timer = setTimeout(
          () => void poll(),
          Math.max(1000, challenge!.pollIntervalSeconds * 1000 * (result.state === 1 ? 2 : 1)),
        )
      } catch (failure) {
        if (!stopped) {
          setError(failure)
          setChallenge(null)
        }
      }
    }
    timer = setTimeout(() => void poll(), Math.max(1000, challenge.pollIntervalSeconds * 1000))
    return () => {
      stopped = true
      clearTimeout(timer)
      void request(
        'plugins.cancel',
        { pluginId: plugin.id },
        { clientId, attemptId: challenge.attemptId },
      ).catch(() => {})
    }
  }, [challenge, client, clientId, plugin.id])
  async function begin() {
    setPending(true)
    setError(null)
    try {
      const result = await request<{ challenge: Challenge | null }>(
        'plugins.signIn',
        { pluginId: plugin.id },
        { clientId },
      )
      setChallenge(result.challenge)
      if (!result.challenge) setMessage('The provider could not start sign-in.')
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  async function signOut() {
    setPending(true)
    setError(null)
    try {
      await request('plugins.signOut', { pluginId: plugin.id })
      await client.invalidateQueries({ queryKey: ['api'] })
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  return (
    <div className="plugin-account">
      {plugin.accountConnected ? (
        <>
          <p>
            <Check size={16} /> Account connected
          </p>
          <button disabled={pending} onClick={() => void signOut()}>
            Sign out
          </button>
        </>
      ) : challenge ? (
        <div className="conflict-panel">
          <p>Enter this code on the provider's sign-in page:</p>
          <strong className="device-code">{challenge.userCode}</strong>
          <div className="form-actions">
            <button
              onClick={() => {
                void openExternal(challenge.verificationUrl)
              }}
            >
              Open sign-in page
            </button>
            <button onClick={() => setChallenge(null)}>Cancel sign-in</button>
          </div>
        </div>
      ) : (
        <button disabled={pending} onClick={() => void begin()}>
          Connect account
        </button>
      )}
      <Notice error={error} message={message} />
    </div>
  )
}

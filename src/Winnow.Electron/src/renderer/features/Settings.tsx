import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Check, RefreshCw } from 'lucide-react'
import { createClientId, dateLabel, request } from '../api/client'
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

export function Settings({ mode = 'desktop' }: { mode?: Mode }) {
  const [tab, setTab] = useViewState(`${mode}:settings:tab`, 'Connections')
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
        {['Connections', 'Providers', 'Library', 'Recommendations', 'Operations'].map((name) => (
          <button key={name} aria-pressed={tab === name} onClick={() => setTab(name)}>
            {name}
          </button>
        ))}
      </nav>
      {tab === 'Connections' && (
        <div className="feature-grid">
          <section className="feature-panel">
            <h2>Steam</h2>
            <p>
              {stores.data?.steam.hasUsableCredential
                ? 'Connected for online metadata.'
                : stores.data?.steam.hasApiKey || stores.data?.steam.sessionUsable
                  ? 'Credentials available.'
                  : 'Local Steam games can be read without an account connection.'}
            </p>
            <Notice error={stores.error} />
            <SteamKeyForm />
            {stores.data?.steam.hasSession && (
              <button
                disabled={command.isPending}
                onClick={() => command.mutate({ route: 'connections.steam.signOut' })}
              >
                Sign out of Steam
              </button>
            )}
            {stores.data?.steam.apiKeyIsAppManaged && (
              <button
                disabled={command.isPending}
                onClick={() => command.mutate({ route: 'connections.steam.key', body: { key: null } })}
              >
                Remove saved API key
              </button>
            )}
          </section>
          <section className="feature-panel">
            <h2>Epic Games</h2>
            <p>
              {stores.data?.epic?.isLive
                ? `Connected${stores.data.epic.displayName ? ` as ${stores.data.epic.displayName}` : ''}.`
                : 'Installed games are available through the local Epic library.'}
            </p>
            {stores.data?.epic ? (
              <button
                disabled={command.isPending}
                onClick={() => command.mutate({ route: 'connections.epic.signOut' })}
              >
                Sign out of Epic
              </button>
            ) : (
              <EpicAccount />
            )}
            <p className="muted">Existing account connections are shared with other Winnow frontends.</p>
          </section>
          <section className="feature-panel">
            <h2>IGDB</h2>
            <p>Descriptions, game identity, and artwork from IGDB.</p>
            <Notice error={igdb.error} />
            {igdb.data && <IgdbForm snapshot={igdb.data} />}
          </section>
          <Notice error={command.error} />
        </div>
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
              <PluginCard key={plugin.id} plugin={plugin} />
            ))}
          </div>
          {plugins.data?.length === 0 && <Empty>No provider plugins are installed.</Empty>}
        </>
      )}
      {tab === 'Library' && (
        <section className="feature-panel">
          <h2>Library visibility</h2>
          <Notice error={preferences.error} />
          {preferences.data && (
            <LibraryPreferenceForm initial={preferences.data} key={JSON.stringify(preferences.data)} />
          )}
        </section>
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

function SteamKeyForm() {
  const [key, setKey] = useState('')
  const [message, setMessage] = useState('')
  const command = useCommand<number>()
  async function save() {
    const result = await command.mutateAsync({ route: 'connections.steam.key', body: { key } })
    setKey('')
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
        <button disabled={command.isPending || !key.trim()}>Save API key</button>
        <button
          type="button"
          onClick={() => {
            void window.winnow.openExternal('https://steamcommunity.com/dev/apikey')
          }}
        >
          Get a key
        </button>
      </div>
      <Notice error={command.error} message={message} />
    </form>
  )
}

function IgdbForm({ snapshot }: { snapshot: IgdbConnection }) {
  const [clientId, setClientId] = useState(snapshot.clientId)
  const [secret, setSecret] = useState('')
  const [message, setMessage] = useState('')
  const command = useCommand<number | boolean>()
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
            void window.winnow.openExternal('https://dev.twitch.tv/console/apps')
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

function LibraryPreferenceForm({ initial }: { initial: LibraryPreferences }) {
  const command = useCommand()
  async function change(field: keyof LibraryPreferences, value: boolean | string) {
    // This endpoint replaces the whole object: read the latest fields before changing one.
    const latest = await request<LibraryPreferences>('preferences.library.get')
    await command.mutateAsync({ route: 'preferences.library.put', body: { ...latest, [field]: value } })
  }
  const [readError, setReadError] = useState<unknown>(null)
  function apply(field: keyof LibraryPreferences, value: boolean | string) {
    setReadError(null)
    void change(field, value).catch(setReadError)
  }
  return (
    <div className="editor-form">
      <label className="check-field">
        <input
          type="checkbox"
          checked={initial.showNonGameEntries}
          disabled={command.isPending}
          onChange={(event) => apply('showNonGameEntries', event.target.checked)}
        />
        Show tools, demos, and other non-game entries
      </label>
      <label className="check-field">
        <input
          type="checkbox"
          checked={initial.showExplicitContent}
          disabled={command.isPending}
          onChange={(event) => apply('showExplicitContent', event.target.checked)}
        />
        Show explicit content
      </label>
      <p className="muted">
        Maturity cap: {initial.maturityCap}. These preferences apply to every frontend attached to this
        library.
      </p>
      <Notice error={readError || command.error} />
    </div>
  )
}

function PluginCard({ plugin }: { plugin: PluginSnapshot }) {
  const [values, setValues] = useState<Record<string, string>>({})
  const [message, setMessage] = useState('')
  const command = useCommand()
  async function save() {
    await command.mutateAsync({
      route: 'plugins.settings',
      params: { pluginId: plugin.id },
      body: { values },
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
            disabled={command.isPending}
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
        {plugin.settings
          .filter((setting) => !setting.isAdvanced)
          .map((setting) => (
            <label className={setting.isBoolean ? 'check-field' : 'field'} key={setting.key}>
              {setting.isBoolean ? (
                <>
                  <input
                    type="checkbox"
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
                    autoComplete="off"
                    value={values[setting.key] ?? (setting.isSecret ? '' : (setting.value ?? ''))}
                    placeholder={setting.hasStoredSecret ? 'Saved secret — leave blank to keep' : undefined}
                    onChange={(event) => {
                      const next = { ...values, [setting.key]: event.target.value }
                      if (setting.isSecret && !event.target.value) delete next[setting.key]
                      setValues(next)
                    }}
                  />
                </>
              )}
              {setting.description && <small>{setting.description}</small>}
            </label>
          ))}
        <div className="form-actions">
          {plugin.canConfigure && plugin.settings.length > 0 && (
            <button disabled={command.isPending || !Object.keys(values).length}>
              Save provider settings
            </button>
          )}
          <button
            type="button"
            disabled={command.isPending || !plugin.enabled}
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
function EpicAccount() {
  const [challenge, setChallenge] = useState<EpicChallenge | null>(null)
  const [accepted, setAccepted] = useState(false)
  const [callback, setCallback] = useState('')
  const [message, setMessage] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [pending, setPending] = useState(false)
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
    <div className="epic-account">
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
              void window.winnow.openExternal(challenge.request.startUrl)
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
          Connect Epic Games
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
                void window.winnow.openExternal(challenge.verificationUrl)
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

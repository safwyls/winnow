import type { ReactNode } from 'react'
import type { StoreConnections } from '../api/types'
import { steamConnectionState } from './steamConnection'
import { AccountVisibility } from './SettingsPreferences'
import { Notice } from './shared'
import './steam-connections.css'

export function SteamConnectionPanel({
  snapshot,
  signIn,
  keyEditor,
  purchase,
  busy,
  onSignOut,
  onClearKey,
  error,
}: {
  snapshot: StoreConnections
  signIn: ReactNode
  keyEditor: ReactNode
  purchase: ReactNode
  busy: boolean
  onSignOut(): void
  onClearKey(): void
  error?: unknown
}) {
  const state = steamConnectionState(snapshot),
    steam = snapshot.steam
  return (
    <section className="feature-panel steam-connections" aria-label="Steam connection">
      <header>
        <h2>Steam</h2>
        <p
          className="connection-state"
          data-tone={state.attention ? 'attention' : state.live ? 'live' : 'quiet'}
        >
          {state.label}
        </p>
      </header>
      <p>{state.connectionMessage}</p>
      <h3>
        Local files <small>On</small>
      </h3>
      <details>
        <summary>What local files cover</summary>
        <p>Always on. Reads playtime and last-played from Steam's local files.</p>
      </details>
      <h3>
        Web API <small>{state.keyInUse ? 'On - API' : state.signInInUse ? 'On - Login' : 'Off'}</small>
      </h3>
      <p>Two ways to connect to Steam's Web API. You only need one.</p>
      {steam.hasApiKey && steam.hasSession && (
        <p>Scheduled updates use the API key because keys do not expire.</p>
      )}
      <details>
        <summary>Which one should I use?</summary>
        <p>
          Sign-in identifies your account and can read your purchase history. It lasts about a day; automatic
          renewal depends on Steam supplying a refresh token.
        </p>
        <p>
          An API key never expires, so scheduled updates keep working. It confirms your account during a Steam
          import and cannot read purchase history.
        </p>
      </details>
      <section aria-label="Steam sign-in method">
        <h3>
          {steam.hasSession && state.health !== 4 ? 'Signed in' : 'Sign in to Steam'}{' '}
          <small>
            {state.terse}
            {state.signInInUse ? ' · In use' : ''}
          </small>
        </h3>
        {state.healthAttention && (
          <p className="connection-warning" role="status">
            {state.healthMessage}
          </p>
        )}
        {steam.sessionAccount && (
          <p className="connection-account">
            Signed in as <span>{steam.sessionAccount}</span>
          </p>
        )}
        {signIn}
        {steam.hasSession && (
          <button disabled={busy} onClick={onSignOut}>
            Sign out of Steam
          </button>
        )}
        <details>
          <summary>About signing in</summary>
          {!state.healthAttention && <p>{state.healthMessage}</p>}
          <p>
            Identifies your account and can read your purchase history. Lasts about a day. Winnow renews it
            automatically, but this may not work against live servers. An API key does not expire.
          </p>
          {steam.sessionExpiresAt && (
            <p>
              Session expires{' '}
              <time dateTime={steam.sessionExpiresAt}>
                {new Date(steam.sessionExpiresAt).toLocaleString()}
              </time>
              .
            </p>
          )}
          <p>
            Signing out deletes the stored session. Your local Steam games stay, and an API key keeps working.
            Winnow also forgets which account the sign-in identified as yours unless a key has already
            confirmed it.
          </p>
        </details>
      </section>
      <section aria-label="Steam API key method">
        <h3>
          Web API key{' '}
          <small>
            {state.keyState}
            {state.keyInUse ? ' · In use' : ''}
          </small>
        </h3>
        {keyEditor}
        {steam.apiKeyIsAppManaged && (
          <button disabled={busy} onClick={onClearKey}>
            Remove saved API key
          </button>
        )}
        <details>
          <summary>About API keys</summary>
          <p>
            Never expires, so scheduled updates keep working. The account filter is unavailable until a Steam
            import confirms your account. A key cannot read your purchase history.
          </p>
          {steam.hasApiKey && !steam.apiKeyIsAppManaged && (
            <p>
              This key is supplied by configuration. Remove it from its source to clear it; Winnow cannot
              clear it here.
            </p>
          )}
        </details>
      </section>
      <h3>Steam accounts</h3>
      <AccountVisibility credentials={steam} />
      <details>
        <summary>What the account filter covers</summary>
        <p>
          The filter uses the Steam account Winnow has confirmed as yours. It can hide games from other local
          accounts; it does not remove ownership or delete games.
        </p>
      </details>
      <details>
        <summary>Import purchase history</summary>
        {purchase}
      </details>
      <Notice error={error} />
    </section>
  )
}

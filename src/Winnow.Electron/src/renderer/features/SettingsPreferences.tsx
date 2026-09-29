import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createClientId, request } from '../api/client'
import { useApiQuery, useCommand } from '../api/hooks'
import type { Mode, StoreConnections } from '../api/types'
import { Notice } from './shared'
import { ApplicationUpdates } from './Updates'
import { useSetupBusy, useSetupPreferenceError } from './settingsState'

export interface PresentationPreferenceValue {
  preference: string
  value: string | null
}
export function usePresentationPreferences() {
  const query = useApiQuery<PresentationPreferenceValue[]>('preferences.presentation.get')
  const command = useCommand()
  const [error, setError] = useState<unknown>(null)
  const client = useQueryClient()
  const values = Object.fromEntries((query.data ?? []).map((row) => [row.preference, row.value]))
  const set = (preference: string, value: string) => {
    setError(null)
    void command
      .mutateAsync({ route: 'preferences.presentation.put', params: { preference }, body: { value } })
      .then(() =>
        client.setQueryData<PresentationPreferenceValue[]>(
          ['api', 'preferences.presentation.get', undefined],
          (rows) => rows?.map((row) => (row.preference === preference ? { ...row, value } : row)),
        ),
      )
      .catch(setError)
  }
  return { values, set, pending: command.isPending, error: error || query.error, loaded: !!query.data }
}

export function ApplicationPreferences({ setup = false }: { setup?: boolean }) {
  const preferences = usePresentationPreferences()
  const command = useCommand()
  const client = useQueryClient()
  const info = useQuery({
    queryKey: ['native', 'applicationInfo'],
    queryFn: () => window.winnow.applicationInfo!(),
    enabled: !!window.winnow.applicationInfo,
  })
  const [nativeError, setNativeError] = useState<unknown>(null)
  const [nativeBusy, setNativeBusy] = useState(false)
  useSetupBusy(preferences.pending || command.isPending || nativeBusy)
  useSetupPreferenceError(preferences.error || nativeError)
  async function autostart(enabled: boolean) {
    setNativeBusy(true)
    setNativeError(null)
    try {
      await window.winnow.setOpenAtLogin!(enabled)
      await info.refetch()
    } catch (failure) {
      setNativeError(failure)
    } finally {
      setNativeBusy(false)
    }
  }
  return (
    <section className="feature-panel">
      <h2>Application</h2>
      <p>Preferences save as you change them.</p>
      {[
        [
          'MinimizeToTray',
          'Minimize to notification area',
          'Hides Winnow from the taskbar when you minimize it.',
        ],
        [
          'CloseToTray',
          'Close to notification area',
          'Keeps Winnow available when you close its window. Use Quit from the notification area to exit.',
        ],
        ['StartInFullscreen', 'Start in fullscreen', 'Open the TV interface on your next launch.'],
      ].map(([key, label, note]) => (
        <label className="check-field" key={key}>
          <input
            type="checkbox"
            checked={preferences.values[key]?.toLowerCase() === 'true'}
            disabled={!preferences.loaded || preferences.pending}
            onChange={(event) => preferences.set(key, String(event.target.checked))}
          />
          <span>
            {label}
            <small>{note}</small>
          </span>
        </label>
      ))}
      {info.data && (
        <label className="check-field">
          <input
            type="checkbox"
            checked={info.data.openAtLogin}
            disabled={!info.data.autostartSupported || nativeBusy}
            onChange={(event) => void autostart(event.target.checked)}
          />
          <span>
            Start with {info.data.platform === 'win32' ? 'Windows' : 'my computer'}
            <small>
              {info.data.autostartSupported
                ? 'Start quietly in the notification area when you sign in.'
                : 'Startup registration is unavailable in this build.'}
            </small>
          </span>
        </label>
      )}
      <label className="field">
        Open links in
        <select
          value={
            preferences.values.LinkDestination === 'store' && info.data?.steamStoreAvailable === false
              ? 'browser'
              : (preferences.values.LinkDestination ?? 'in-app')
          }
          disabled={!preferences.loaded || preferences.pending}
          onChange={(event) => preferences.set('LinkDestination', event.target.value)}
        >
          <option value="in-app">In Winnow</option>
          <option value="browser">Default browser</option>
          {info.data?.steamStoreAvailable === true && (
            <option value="store">Steam client, when available</option>
          )}
        </select>
      </label>
      {!setup && (
        <div className="form-actions">
          <button
            disabled={command.isPending}
            onClick={() =>
              command.mutate(
                { route: 'setup.put', body: { step: 0 } },
                {
                  onSuccess: () =>
                    client.setQueryData(['api', 'setup.get', undefined], { step: 0, problem: null }),
                },
              )
            }
          >
            Run setup again
          </button>
          {window.winnow.openDataFolder && (
            <button onClick={() => void window.winnow.openDataFolder!('logs').catch(setNativeError)}>
              Open logs folder
            </button>
          )}
        </div>
      )}
      {!setup && info.data && <p className="muted">Winnow {info.data.version}</p>}
      {!setup && <ApplicationUpdates />}
      <Notice error={preferences.error || command.error || nativeError || info.error} />
    </section>
  )
}

export function LibraryPresentationPreferences() {
  const preferences = usePresentationPreferences()
  useSetupBusy(preferences.pending)
  useSetupPreferenceError(preferences.error)
  return (
    <section className="feature-panel">
      <h2>Library presentation</h2>
      <label className="field">
        Default library sort
        <select
          value={preferences.values.DefaultSort ?? 'DormantLongest'}
          disabled={!preferences.loaded || preferences.pending}
          onChange={(event) => preferences.set('DefaultSort', event.target.value)}
        >
          {Object.entries({
            DormantLongest: 'Dormant longest',
            RecentlyPlayed: 'Recently played',
            PlaytimeHighToLow: 'Playtime high to low',
            PlaytimeLowToHigh: 'Playtime low to high',
            NameAscending: 'Name A–Z',
            NameDescending: 'Name Z–A',
          }).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        Preferred platform for grouped games
        <select
          value={preferences.values.PreferredMergePlatform ?? ''}
          disabled={!preferences.loaded || preferences.pending}
          onChange={(event) => preferences.set('PreferredMergePlatform', event.target.value)}
        >
          <option value="">No preference</option>
          <option value="steam">Steam</option>
          <option value="epic">Epic Games</option>
          <option value="gog">GOG</option>
        </select>
      </label>
      <label className="field">
        Cover artwork
        <select
          value={preferences.values.CoverArtMode ?? 'fit'}
          disabled={!preferences.loaded || preferences.pending}
          onChange={(event) => preferences.set('CoverArtMode', event.target.value)}
        >
          <option value="fit">Fit the whole cover</option>
          <option value="fill">Fill the cover frame</option>
        </select>
      </label>
      <label className="check-field">
        <input
          type="checkbox"
          checked={preferences.values.GroupExpansions?.toLowerCase() === 'true'}
          disabled={!preferences.loaded || preferences.pending}
          onChange={(event) => preferences.set('GroupExpansions', String(event.target.checked))}
        />
        Group expansions with their base game
      </label>
      <label className="check-field">
        <input
          type="checkbox"
          checked={preferences.values.DimDormantCovers?.toLowerCase() !== 'false'}
          disabled={!preferences.loaded || preferences.pending}
          onChange={(event) => preferences.set('DimDormantCovers', String(event.target.checked))}
        />
        Dim dormant covers
      </label>
      <JournalPromptPreference />
      <Notice error={preferences.error} />
    </section>
  )
}

export function JournalPromptPreference() {
  const query = useApiQuery<{ promptAfterPlay: boolean }>('journal.preferences.get')
  const command = useCommand()
  useSetupBusy(command.isPending)
  useSetupPreferenceError(command.error)
  return (
    <div>
      <label className="check-field">
        <input
          type="checkbox"
          checked={query.data?.promptAfterPlay ?? false}
          disabled={!query.data || command.isPending}
          onChange={(event) =>
            command.mutate({
              route: 'journal.preferences.put',
              body: { promptAfterPlay: event.target.checked },
            })
          }
        />
        <span>
          Ask for a note after playing
          <small>Offer a short journal note and rating when a recorded session ends. Off by default.</small>
        </span>
      </label>
      <Notice error={query.error || command.error} />
    </div>
  )
}

export function FullscreenPreferences({ mode }: { mode: Mode }) {
  const preferences = usePresentationPreferences()
  const client = useQueryClient()
  const [confirmReset, setConfirmReset] = useState(false)
  const [resetting, setResetting] = useState(false)
  const [resetError, setResetError] = useState<unknown>(null)
  async function reset() {
    setResetting(true)
    setResetError(null)
    try {
      for (const [preference, value] of [
        ['FullscreenTextScale', '1'],
        ['FullscreenInterfaceScale', '1'],
        ['FullscreenSafeMargin', '5'],
        ['FullscreenReducedMotion', 'false'],
        ['FullscreenFitUltrawide', 'false'],
      ])
        await request('preferences.presentation.put', { preference }, { value })
      setConfirmReset(false)
    } catch (failure) {
      setResetError(failure)
    } finally {
      await client.invalidateQueries({ queryKey: ['api', 'preferences.presentation.get'] })
      setResetting(false)
    }
  }
  return (
    <section className="feature-panel">
      <h2>Fullscreen appearance</h2>
      <p>These settings apply to the TV interface{mode === 'fullscreen' ? ' immediately' : ''}.</p>
      {[
        ['FullscreenTextScale', 'Text size', 0.7, 1.4, 0.05, 1],
        ['FullscreenInterfaceScale', 'Interface scale', 0.8, 1.2, 0.05, 1],
        ['FullscreenSafeMargin', 'Screen edge margin', 0, 10, 1, 5],
      ].map(([key, label, min, max, step, fallback]) => {
        const value = Number(preferences.values[String(key)] ?? fallback)
        return (
          <label className="field" key={key}>
            {label} · {Math.round(value * (key === 'FullscreenSafeMargin' ? 1 : 100))}%
            <input
              type="range"
              min={min}
              max={max}
              step={step}
              value={value}
              disabled={!preferences.loaded || preferences.pending}
              onChange={(event) => preferences.set(String(key), event.target.value)}
            />
          </label>
        )
      })}
      {[
        ['FullscreenReducedMotion', 'Reduce motion'],
        ['FullscreenFitUltrawide', 'Fit ultrawide displays'],
      ].map(([key, label]) => (
        <label className="check-field" key={key}>
          <input
            type="checkbox"
            checked={preferences.values[key]?.toLowerCase() === 'true'}
            disabled={!preferences.loaded || preferences.pending || resetting}
            onChange={(event) => preferences.set(key, String(event.target.checked))}
          />
          {label}
        </label>
      ))}
      <button disabled={preferences.pending || resetting} onClick={() => setConfirmReset(true)}>
        Reset fullscreen appearance…
      </button>
      {confirmReset && (
        <section role="alertdialog" aria-label="Reset fullscreen appearance" className="conflict-panel">
          <p>
            Reset fullscreen text size, interface scale, screen margins, reduced motion and ultrawide fit?
          </p>
          <div className="form-actions">
            <button disabled={resetting} onClick={() => void reset()}>
              Reset fullscreen appearance
            </button>
            <button disabled={resetting} onClick={() => setConfirmReset(false)}>
              Cancel
            </button>
          </div>
        </section>
      )}
      <Notice error={preferences.error || resetError} />
    </section>
  )
}

export function AccountVisibility({ credentials }: { credentials?: StoreConnections['steam'] } = {}) {
  const state = useApiQuery<{ accountConfirmed: boolean; ownAccountOnly: boolean; hiddenCount: number }>(
    'connections.visibility.get',
  )
  const command = useCommand()
  return (
    <div>
      <label className="check-field">
        <input
          type="checkbox"
          checked={state.data?.ownAccountOnly ?? false}
          disabled={!state.data?.accountConfirmed || command.isPending}
          onChange={(event) =>
            command.mutate({
              route: 'connections.visibility.put',
              body: { ownAccountOnly: event.target.checked },
            })
          }
        />
        Only show games from my Steam account
      </label>
      <p className="muted">
        {state.data?.accountConfirmed
          ? `${state.data.hiddenCount} games from other accounts are affected.`
          : credentials?.hasSession
            ? 'The sign-in did not record which account is yours. Signing in again should resolve this.'
            : credentials?.hasApiKey
              ? 'Your API key is set, but Winnow has not confirmed which account it belongs to yet. This happens automatically during the next Steam import.'
              : credentials
                ? 'Winnow does not know which Steam account is yours yet. Signing in tells it immediately; an API key finds out at the next Steam import.'
                : 'This becomes available after Steam confirms which account is yours.'}
      </p>
      <Notice error={state.error || command.error} />
    </div>
  )
}

export function ArtworkSourcePreferences() {
  const preferences = usePresentationPreferences()
  const sources = useApiQuery<{ id: string; label: string }[]>('preferences.artworkSources')
  const available = sources.data ?? []
  const order = [
    ...new Set([
      ...(preferences.values.ArtworkSourceOrder?.split(',') ?? []).map((id) =>
        id === 'steamgriddb' && available.some((source) => source.id === 'plugin:steamgriddb')
          ? 'plugin:steamgriddb'
          : id,
      ),
      ...available.map((source) => source.id),
    ]),
  ].filter((id) => available.some((source) => source.id === id))
  function move(index: number, delta: number) {
    const next = [...order]
    ;[next[index], next[index + delta]] = [next[index + delta], next[index]]
    preferences.set('ArtworkSourceOrder', next.join(','))
  }
  return (
    <section className="feature-panel">
      <h2>Background artwork sources</h2>
      <p>
        Try sources from top to bottom. Your saved background always comes first. Standard Steam heroes and
        covers remain fallbacks.
      </p>
      <ol className="artwork-source-order">
        {order.map((id, index) => {
          const label = available.find((source) => source.id === id)!.label
          return (
            <li key={id}>
              <span>{label}</span>
              <div className="form-actions">
                <button
                  disabled={index === 0 || preferences.pending}
                  aria-label={`Move ${label} up`}
                  onClick={() => move(index, -1)}
                >
                  Move up
                </button>
                <button
                  disabled={index === order.length - 1 || preferences.pending}
                  aria-label={`Move ${label} down`}
                  onClick={() => move(index, 1)}
                >
                  Move down
                </button>
              </div>
            </li>
          )
        })}
      </ol>
      <Notice error={sources.error || preferences.error} />
    </section>
  )
}

export function OfficialPluginInstall({
  initialRequest,
}: { initialRequest?: { pluginId: string; releaseTag: string } } = {}) {
  const [pluginId, setPluginId] = useState(initialRequest?.pluginId ?? 'steamgriddb')
  const [releaseTag, setReleaseTag] = useState(initialRequest?.releaseTag ?? '')
  const [operationId, setOperationId] = useState<string | null>(null)
  const command = useCommand()
  const operation = useApiQuery<{ state: string; message: string }>(
    'operations.detail',
    { id: operationId ?? '' },
    !!operationId,
  )
  useEffect(() => {
    if (
      !operationId ||
      (operation.data && !['queued', 'running'].includes(operation.data.state.toLowerCase()))
    )
      return
    const timer = setInterval(() => void operation.refetch(), 1500)
    return () => clearInterval(timer)
  }, [operationId, operation.data?.state, operation.refetch])
  const [error, setError] = useState<unknown>(null)
  const client = useQueryClient()
  async function install() {
    setError(null)
    // Retain the operation identity after an uncertain response to avoid duplicate installs.
    const id =
      operation.data?.state.toLowerCase() === 'failed' ? createClientId() : (operationId ?? createClientId())
    setOperationId(id)
    try {
      await command.mutateAsync({
        route: 'operations.plugin',
        body: { operationId: id, request: { pluginId, releaseTag } },
      })
      await client.invalidateQueries({ queryKey: ['api', 'operations.detail'] })
    } catch (failure) {
      setError(failure)
    }
  }
  const active = operation.data && ['Queued', 'Running', 'queued', 'running'].includes(operation.data.state)
  return (
    <section className="feature-panel">
      <h2>Install an official provider</h2>
      <p>
        Choose an official provider and its published release tag. Packages are verified before installation.
      </p>
      <p className="muted">
        You can also place a plugin ZIP or unpacked plugin in the plugins folder. ZIPs unpack when the library
        service restarts. Manually added plugins need enabling and another service restart. Only enable
        plugins from authors you trust: plugins run with Winnow’s access to this device.
      </p>
      {window.winnow.openDataFolder && (
        <button onClick={() => void window.winnow.openDataFolder!('plugins').catch(setError)}>
          Open plugins folder
        </button>
      )}
      <form
        onSubmit={(event) => {
          event.preventDefault()
          void install()
        }}
      >
        <label className="field">
          Provider
          <select
            value={pluginId}
            disabled={!!active || command.isPending}
            onChange={(event) => {
              setPluginId(event.target.value)
              setOperationId(null)
            }}
          >
            <option value="steamgriddb">SteamGridDB</option>
            <option value="psn">PlayStation Network</option>
            <option value="xbox">Xbox</option>
          </select>
        </label>
        <label className="field">
          Release tag
          <input
            placeholder="v1.2.3"
            required
            pattern="v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?"
            maxLength={80}
            value={releaseTag}
            disabled={!!active || command.isPending}
            onChange={(event) => {
              setReleaseTag(event.target.value)
              setOperationId(null)
            }}
          />
        </label>
        <button disabled={!!active || command.isPending || !releaseTag.trim()}>
          {error ? 'Retry installation' : 'Install provider'}
        </button>
        {active && (
          <button
            type="button"
            onClick={() =>
              void request('operations.cancel', { id: operationId! })
                .then(() => client.invalidateQueries({ queryKey: ['api'] }))
                .catch(setError)
            }
          >
            Cancel installation
          </button>
        )}
      </form>
      <Notice error={error} message={operation.data?.message} />
    </section>
  )
}

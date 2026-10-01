import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { request } from '../api/client'
import { useApiQuery, useCommand } from '../api/hooks'
import type { Mode, StoreConnections } from '../api/types'
import { Notice } from './shared'
import { ApplicationUpdates } from './Updates'
import { useSetupBusy, useSetupPreferenceError } from './settingsState'
import { parseExpansionGrouping } from './parity-library-grain'
import { usePluginInstallation } from './plugin-installation'
import { PluginInstallStatus } from './PluginInstallStatus'
import { useViewState } from '../viewState'
import './application-info.css'
import {
  FullscreenAdjustment,
  FullscreenSettingsAction,
  FullscreenSwitch,
  useFullscreenSettingsEntry,
} from './FullscreenSettingRows'

export interface PresentationPreferenceValue {
  preference: string
  value: string | null
}
export function usePresentationPreferences() {
  const query = useApiQuery<PresentationPreferenceValue[]>('preferences.presentation.get')
  const command = useCommand()
  const [error, setError] = useState<unknown>(null)
  const client = useQueryClient()
  const changedControl = useRef<HTMLElement | null>(null)
  useLayoutEffect(() => {
    if (command.isPending) {
      const cancel = () => {
        changedControl.current = null
      }
      document.addEventListener('pointerdown', cancel, true)
      document.addEventListener('keydown', cancel, true)
      return () => {
        document.removeEventListener('pointerdown', cancel, true)
        document.removeEventListener('keydown', cancel, true)
      }
    }
    const control = changedControl.current
    changedControl.current = null
    // Chromium blurs disabled fields while the shared preference is saved.
    if (control?.isConnected && document.activeElement === document.body)
      control.focus({ preventScroll: true })
  }, [command.isPending])
  const values = Object.fromEntries((query.data ?? []).map((row) => [row.preference, row.value]))
  const set = (preference: string, value: string) => {
    if (preference === 'CoverArtMode' && value !== 'fit' && value !== 'fill') return Promise.resolve(false)
    const active = document.activeElement
    changedControl.current = active instanceof HTMLElement && active !== document.body ? active : null
    setError(null)
    return command
      .mutateAsync({ route: 'preferences.presentation.put', params: { preference }, body: { value } })
      .then(() => {
        client.setQueryData<PresentationPreferenceValue[]>(
          ['api', 'preferences.presentation.get', undefined],
          (rows) => [...(rows ?? []).filter((row) => row.preference !== preference), { preference, value }],
        )
        return true
      })
      .catch((error) => {
        setError(error)
        return false
      })
  }
  return { values, set, pending: command.isPending, error: error || query.error, loaded: !!query.data }
}

export function ApplicationPreferences({
  setup = false,
  mode = 'desktop',
}: {
  setup?: boolean
  mode?: Mode
}) {
  const [, setSetupSuspended] = useViewState('setup:suspended', false)
  const preferences = usePresentationPreferences()
  const command = useCommand()
  const client = useQueryClient()
  const directory = useApiQuery<{ directory: string }>(
    'plugins.directory',
    undefined,
    mode === 'fullscreen' && !setup,
  )
  // Resolve the backend's active location independently of native startup information.
  const dataPrefix = directory.data?.directory?.match(/^(.*[\\/])plugins[\\/]?$/i)?.[1]
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
    if (!info.data?.autostartSupported || nativeBusy) return
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
  const fullscreen = mode === 'fullscreen'
  const entry = useFullscreenSettingsEntry(fullscreen && preferences.loaded)
  const linkOptions = [
    ['in-app', 'In Winnow'],
    ['browser', 'Default browser'],
    ...(info.data?.steamStoreAvailable ? [['store', 'Steam client, when available']] : []),
  ]
  const currentLink =
    preferences.values.LinkDestination === 'store' && info.data?.steamStoreAvailable === false
      ? 'browser'
      : (preferences.values.LinkDestination ?? 'in-app')
  const linkIndex = Math.max(
    0,
    linkOptions.findIndex(([value]) => value === currentLink),
  )
  const replay = () =>
    command.mutate(
      { route: 'setup.put', body: { step: 0 } },
      {
        onSuccess: () => client.setQueryData(['api', 'setup.get', undefined], { step: 0, problem: null }),
        onError: () =>
          client.setQueryData(['setup-recovery'], {
            step: 0,
            problem:
              'Could not save setup progress. Continue or Skip setup to try again. Your saved preferences are unchanged.',
          }),
        onSettled: () => setSetupSuspended(false),
      },
    )
  return (
    <section
      ref={entry}
      className={fullscreen ? 'fullscreen-settings-content' : 'feature-panel'}
      aria-label="Application preferences"
    >
      <h2 className={fullscreen ? 'fullscreen-settings-group' : undefined}>
        {fullscreen ? 'Startup & window' : 'Application'}
      </h2>
      {!fullscreen && <p className="reading-prose">Preferences save as you change them.</p>}
      {[
        [
          'MinimizeToTray',
          fullscreen ? 'Minimize to tray' : 'Minimize to notification area',
          fullscreen
            ? 'Keep Winnow running when minimized.'
            : 'Hides Winnow from the taskbar when you minimize it.',
        ],
        [
          'CloseToTray',
          fullscreen ? 'Close to tray' : 'Close to notification area',
          fullscreen
            ? 'Keep Winnow running when its window is closed.'
            : 'Keeps Winnow available when you close its window. Use Quit from the notification area to exit.',
        ],
        ['StartInFullscreen', 'Start in fullscreen', 'Open the TV interface on your next launch.'],
      ]
        .sort((a, b) =>
          fullscreen ? Number(b[0] === 'StartInFullscreen') - Number(a[0] === 'StartInFullscreen') : 0,
        )
        .map(([key, label, note]) =>
          fullscreen ? (
            <FullscreenSwitch
              key={key}
              label={label}
              description={note}
              value={preferences.values[key]?.toLowerCase() === 'true'}
              disabled={!preferences.loaded || preferences.pending}
              change={(value) => preferences.set(key, String(value))}
            />
          ) : (
            <label className="check-field" key={key}>
              <input
                type="checkbox"
                checked={preferences.values[key]?.toLowerCase() === 'true'}
                disabled={!preferences.loaded || preferences.pending}
                onChange={(event) => preferences.set(key, String(event.target.checked))}
              />
              <span>
                {label}
                <small className="reading-prose">{note}</small>
              </span>
            </label>
          ),
        )}
      {info.data &&
        (!fullscreen || info.data.autostartSupported) &&
        (fullscreen ? (
          <FullscreenSwitch
            label={`Start with ${info.data.platform === 'win32' ? 'Windows' : 'my computer'}`}
            description={
              info.data.autostartSupported
                ? 'Start quietly in the notification area when you sign in.'
                : 'Startup registration is unavailable in this build.'
            }
            value={info.data.openAtLogin}
            disabled={!info.data.autostartSupported || nativeBusy}
            change={(value) => {
              void autostart(value)
            }}
          />
        ) : (
          <label className="check-field">
            <input
              type="checkbox"
              checked={info.data.openAtLogin}
              disabled={!info.data.autostartSupported || nativeBusy}
              onChange={(event) => void autostart(event.target.checked)}
            />
            <span>
              Start with {info.data.platform === 'win32' ? 'Windows' : 'my computer'}
              <small className="reading-prose">
                {info.data.autostartSupported
                  ? 'Start quietly in the notification area when you sign in.'
                  : 'Startup registration is unavailable in this build.'}
              </small>
            </span>
          </label>
        ))}
      {fullscreen && <h2 className="fullscreen-settings-group">Links</h2>}
      {fullscreen ? (
        <FullscreenAdjustment
          label="Open links in"
          description="Choose where store pages and external links open."
          value={linkOptions[linkIndex]![1]}
          disabled={!preferences.loaded || preferences.pending}
          change={(direction) =>
            preferences.set(
              'LinkDestination',
              linkOptions[(linkIndex + direction + linkOptions.length) % linkOptions.length]![0],
            )
          }
        />
      ) : (
        <label className="field">
          Open links in
          <select
            aria-label="Open links in"
            aria-description="Choose where store pages and external links open."
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
          <small className="reading-prose">Choose where store pages and external links open.</small>
        </label>
      )}
      {!setup &&
        (fullscreen ? (
          <>
            <h2 className="fullscreen-settings-group">Diagnostics</h2>
            {window.winnow.openDataFolder && (
              <FullscreenSettingsAction
                label="Open logs folder"
                onClick={() => {
                  void window.winnow.openDataFolder!('logs').catch(setNativeError)
                }}
              />
            )}
            <p className="muted">
              For a bug report, include the Winnow version, what happened and when, and the recent log files.
            </p>
            {dataPrefix && <p className="muted fullscreen-settings-path">{dataPrefix}logs</p>}
            <h2 className="fullscreen-settings-group">Tools</h2>
            <FullscreenSettingsAction label="Run setup again" onClick={replay} disabled={command.isPending} />
          </>
        ) : (
          <div className="form-actions">
            <button disabled={command.isPending} onClick={replay}>
              Run setup again
            </button>
            {window.winnow.openDataFolder && (
              <button onClick={() => void window.winnow.openDataFolder!('logs').catch(setNativeError)}>
                Open logs folder
              </button>
            )}
          </div>
        ))}
      {!setup && info.data && (
        <section className={`application-build-info mode-${mode}`} aria-label="About Winnow">
          <h2 className={fullscreen ? 'fullscreen-settings-group' : undefined}>About Winnow</h2>
          <dl>
            <div>
              <dt>Version</dt>
              <dd>{info.data.version}</dd>
            </div>
            <div>
              <dt>Source commit</dt>
              <dd>{info.data.commit ?? 'Unavailable'}</dd>
            </div>
          </dl>
        </section>
      )}
      {!setup && <ApplicationUpdates mode={mode} />}
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
      <p className="reading-prose">
        The order used when Winnow starts. You can still change the sort while browsing.
      </p>
      <label className="field">
        Default library sort
        <select
          value={
            [
              'DormantLongest',
              'RecentlyPlayed',
              'PlaytimeHighToLow',
              'PlaytimeLowToHigh',
              'NameAscending',
              'NameDescending',
            ].includes(preferences.values.DefaultSort ?? '')
              ? preferences.values.DefaultSort!
              : 'DormantLongest'
          }
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
          value={preferences.values.CoverArtMode === 'fill' ? 'fill' : 'fit'}
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
          checked={parseExpansionGrouping(preferences.values.GroupExpansions)}
          disabled={!preferences.loaded || preferences.pending}
          onChange={(event) => preferences.set('GroupExpansions', String(event.target.checked))}
        />
        Group expansions with their base game
      </label>
      <label className="check-field">
        <input
          type="checkbox"
          checked={preferences.values.DimDormantCovers?.trim().toLowerCase() !== 'false'}
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

export function JournalPromptPreference({ mode = 'desktop' }: { mode?: Mode } = {}) {
  const query = useApiQuery<{ promptAfterPlay: boolean }>('journal.preferences.get')
  const command = useCommand()
  useSetupBusy(command.isPending)
  useSetupPreferenceError(command.error)
  return (
    <div>
      {mode === 'fullscreen' ? (
        <FullscreenSwitch
          label="Journal after playing"
          description="Ask for a note after a session."
          value={query.data?.promptAfterPlay ?? false}
          disabled={!query.data || command.isPending}
          change={(value) =>
            command.mutate({ route: 'journal.preferences.put', body: { promptAfterPlay: value } })
          }
        />
      ) : (
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
      )}
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
      <p className="reading-prose">
        These settings apply to the TV interface{mode === 'fullscreen' ? ' immediately' : ''}.
      </p>
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

export function AccountVisibility({
  credentials,
  mode = 'desktop',
}: { credentials?: StoreConnections['steam']; mode?: Mode } = {}) {
  const state = useApiQuery<{ accountConfirmed: boolean; ownAccountOnly: boolean; hiddenCount: number }>(
    'connections.visibility.get',
  )
  const command = useCommand()
  useSetupBusy(command.isPending)
  useSetupPreferenceError(state.error || command.error)
  return (
    <div>
      {mode === 'fullscreen' ? (
        <FullscreenSwitch
          label="Only show games from my Steam account"
          description={
            state.data?.accountConfirmed
              ? 'Hide games from other Steam accounts on this computer.'
              : 'The account filter becomes available after Steam confirms which account is yours.'
          }
          value={state.data?.ownAccountOnly ?? false}
          disabled={!state.data?.accountConfirmed || command.isPending}
          change={(value) =>
            command.mutate({ route: 'connections.visibility.put', body: { ownAccountOnly: value } })
          }
        />
      ) : (
        <label className="check-field">
          <input
            type="checkbox"
            checked={state.data?.ownAccountOnly ?? false}
            title={
              state.data?.accountConfirmed
                ? 'Hide games from other Steam accounts on this computer.'
                : 'The account filter becomes available after Steam confirms which account is yours.'
            }
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
      )}
      {!state.data?.accountConfirmed && <p className="muted">Account confirmation pending</p>}
      <p className="muted reading-prose">
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

export const artworkSourceExplanation =
  'Try sources from top to bottom. Your saved background always comes first. Standard Steam heroes and covers remain fallbacks.'
export function ArtworkSourcePreferences({ mode = 'desktop' }: { mode?: Mode } = {}) {
  const preferences = usePresentationPreferences()
  const sources = useApiQuery<{ id: string; label: string }[]>('preferences.artworkSources')
  const surface = useFullscreenSettingsEntry(mode === 'fullscreen' && preferences.loaded && sources.isSuccess)
  const working = useRef(false)
  const restore = useRef<{ id: string; direction: number; origin: Element } | null>(null)
  const [status, setStatus] = useViewState('artwork:source-order:status', '')
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
  useLayoutEffect(() => {
    if (preferences.pending || !restore.current) return
    const target = restore.current
    restore.current = null
    if (
      !surface.current ||
      (document.activeElement !== document.body && document.activeElement !== target.origin)
    )
      return
    const buttons = [...surface.current.querySelectorAll<HTMLButtonElement>('button[data-source-id]')].filter(
      (button) => button.dataset.sourceId === target.id && !button.disabled,
    )
    const button =
      buttons.find((button) => Number(button.dataset.direction) === target.direction) ?? buttons[0]
    button?.focus({ preventScroll: true })
    button?.scrollIntoView?.({ block: 'nearest' })
  }, [preferences.pending, order.join(','), status])
  async function move(index: number, delta: number) {
    if (
      working.current ||
      !preferences.loaded ||
      preferences.pending ||
      index + delta < 0 ||
      index + delta >= order.length
    )
      return
    working.current = true
    const active = document.activeElement
    if (active && surface.current?.contains(active))
      restore.current = { id: order[index], direction: delta, origin: active }
    setStatus('')
    const next = [...order]
    ;[next[index], next[index + delta]] = [next[index + delta], next[index]]
    const saved = await preferences.set('ArtworkSourceOrder', next.join(','))
    setStatus(
      saved
        ? 'Artwork source order saved.'
        : "Could not save artwork source order. Check that Winnow's data folder is writable, then try again.",
    )
    working.current = false
  }
  return (
    <section
      ref={surface}
      className={mode === 'fullscreen' ? 'fullscreen-artwork-order' : 'feature-panel'}
      aria-label={mode === 'desktop' ? 'Artwork source order' : undefined}
    >
      {mode === 'desktop' && <h2>Artwork source order</h2>}
      <p>{artworkSourceExplanation}</p>
      {mode === 'fullscreen' && (
        <>
          <hr className="fullscreen-information-rule" />
          <h2 className="fullscreen-information-heading">Sources · preferred first</h2>
        </>
      )}
      <ol className="artwork-source-order">
        {order.map((id, index) => {
          const label = available.find((source) => source.id === id)!.label
          return (
            <li key={id}>
              <span>{label}</span>
              <div className="form-actions">
                <button
                  disabled={index === 0 || !preferences.loaded || preferences.pending}
                  aria-label={`Move ${label} up`}
                  data-source-id={id}
                  data-direction={-1}
                  onClick={() => move(index, -1)}
                >
                  Move up
                </button>
                <button
                  disabled={index === order.length - 1 || !preferences.loaded || preferences.pending}
                  aria-label={`Move ${label} down`}
                  data-source-id={id}
                  data-direction={1}
                  onClick={() => move(index, 1)}
                >
                  Move down
                </button>
              </div>
            </li>
          )
        })}
      </ol>
      <p role="status" aria-live="polite" aria-atomic="true">
        {status}
      </p>
      <Notice error={sources.error || (!preferences.loaded && preferences.error)} />
    </section>
  )
}

export function OfficialPluginInstall({
  initialRequest,
  showManualHelp = true,
  showStatus = true,
  mode = 'desktop',
  onBusyChange,
}: {
  initialRequest?: { pluginId: string; releaseTag: string }
  showManualHelp?: boolean
  showStatus?: boolean
  mode?: Mode
  onBusyChange?: (busy: boolean) => void
} = {}) {
  const [pluginId, setPluginId] = useState(initialRequest?.pluginId ?? 'steamgriddb')
  const [releaseTag, setReleaseTag] = useState(initialRequest?.releaseTag ?? '')
  const state = usePluginInstallation()
  const [, setInstallationPage] = useViewState(`${mode}:plugins:installation`, false)
  const [, setFollowInstallation] = useViewState(`${mode}:plugins:installation-follow`, false)
  const [, setInstalledPluginPage] = useViewState<string | null>(`${mode}:plugins:installed`, null)
  const [error, setError] = useState<unknown>(null)
  async function install() {
    setError(null)
    await state.installation.install(
      { pluginId, releaseTag },
      {
        started: () => {
          setFollowInstallation(true)
          setInstallationPage(true)
          setInstalledPluginPage(null)
        },
      },
    )
  }
  useEffect(() => {
    onBusyChange?.(state.busy)
  }, [state.busy, onBusyChange])
  return (
    <section className="feature-panel">
      <h2>Install an official provider</h2>
      <p>
        Choose an official provider and its published release tag. Packages are verified before installation.
      </p>
      {showManualHelp && (
        <p className="muted">
          You can also place a plugin ZIP or unpacked plugin in the plugins folder. ZIPs unpack when the
          library service restarts. Manually added plugins need enabling and another service restart. Only
          enable plugins from authors you trust: plugins run with Winnow’s access to this device.
        </p>
      )}
      {showManualHelp && window.winnow.openDataFolder && (
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
            disabled={state.busy}
            onChange={(event) => {
              setPluginId(event.target.value)
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
            disabled={state.busy}
            onChange={(event) => {
              setReleaseTag(event.target.value)
            }}
          />
        </label>
        <button disabled={state.busy || !releaseTag.trim()}>Install provider</button>
      </form>
      <Notice error={error} />
      {showStatus && <PluginInstallStatus />}
    </section>
  )
}

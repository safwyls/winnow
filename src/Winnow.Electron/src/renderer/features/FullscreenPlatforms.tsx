import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useApiQuery, useCommand, useLibrary } from '../api/hooks'
import type { StoreConnections } from '../api/types'
import { EpicConnectionCard, SteamConnectionCard, SteamKeyForm } from './Settings'
import { FullscreenSettingsAction } from './FullscreenSettingRows'
import { PlatformHints, platformVerticalFocus } from './PlatformControls'
import { steamConnectionState } from './steamConnection'
import { SteamAccountOperation, useSteamAccountBusy } from './SteamAccountOperation'
import { SteamPageImport } from './SteamAccountImport'
import { SteamCapture } from './SteamCapture'
import { Notice } from './shared'
import { titlesByStore } from './Platforms'
import { useLibraryProjection } from './parity-library-projection'

type Page =
  'Platforms' | 'Steam' | 'Epic' | 'GOG' | 'Steam Web API key' | 'Purchase history' | 'Saved Steam pages'
const parents: Partial<Record<Page, Page>> = {
  Steam: 'Platforms',
  Epic: 'Platforms',
  GOG: 'Platforms',
  'Steam Web API key': 'Steam',
  'Purchase history': 'Steam',
  'Saved Steam pages': 'Purchase history',
}

export function FullscreenPlatforms({ onChildChange }: { onChildChange(child: boolean): void }) {
  const [page, setPage] = useState<Page>('Platforms')
  const stores = useApiQuery<StoreConnections>('connections.get')
  const client = useQueryClient()
  const root = useRef<HTMLElement>(null)
  const returnFocus = useRef<string | null>(null)
  const library = useLibrary()
  const projection = useLibraryProjection()
  const counts = library.data ? titlesByStore(projection.games) : undefined
  useEffect(() => {
    if (!['Platforms', 'Steam', 'Epic', 'GOG'].includes(page)) return
    let active = true
    void client
      .cancelQueries({ queryKey: ['api', 'connections.get'] })
      .then(() => (active ? client.invalidateQueries({ queryKey: ['api', 'connections.get'] }) : undefined))
    return () => {
      active = false
    }
  }, [client, page])
  useLayoutEffect(() => {
    onChildChange(page !== 'Platforms')
  }, [page, onChildChange])
  useEffect(() => () => onChildChange(false), [onChildChange])
  useEffect(() => {
    if (!stores.data) return
    const timer = setTimeout(() => {
      if (root.current?.closest('[inert]')) return
      const buttons = [...(root.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? [])]
      const label = returnFocus.current
      const target = label
        ? buttons.find((button) => (button.getAttribute('aria-label') ?? button.textContent) === label)
        : buttons[0]
      target?.focus()
      returnFocus.current = null
    }, 0)
    return () => clearTimeout(timer)
  }, [page, !!stores.data])
  const open = (next: Page) => setPage(next)
  const back = () => {
    returnFocus.current = page === 'Saved Steam pages' ? 'Read saved pages' : page
    setPage(parents[page] ?? 'Platforms')
  }
  const snapshot = stores.data
  if (page === 'Platforms')
    return (
      <section
        ref={root}
        className="fullscreen-platform-summary fullscreen-settings-content"
        aria-label="Platforms"
      >
        <Notice error={stores.error} />
        {snapshot ? (
          (['Steam', 'Epic', 'GOG'] as const).map((platform) => {
            const state = platform === 'Steam' ? steamConnectionState(snapshot) : null
            const status =
              state?.label ??
              (platform === 'Epic'
                ? snapshot.epic?.isLive
                  ? 'SIGNED IN'
                  : snapshot.epic
                    ? 'SESSION EXPIRED'
                    : 'NOT SIGNED IN'
                : 'Not needed')
            const attention =
              state?.attention ?? (platform === 'Epic' && !!snapshot.epic && !snapshot.epic.isLive)
            return (
              <FullscreenSettingsAction
                key={platform}
                label={platform}
                value={status}
                onClick={() => open(platform)}
              >
                {attention && (
                  <span className="platform-attention" aria-label="Needs attention">
                    ●
                  </span>
                )}
              </FullscreenSettingsAction>
            )
          })
        ) : (
          <p role="status">Loading platform connections…</p>
        )}
      </section>
    )
  return (
    <section
      ref={root}
      className="fullscreen-platform-page"
      aria-label={['Steam', 'Epic', 'GOG'].includes(page) ? `${page} connection page` : page}
      onKeyDown={(event) => {
        if (
          event.defaultPrevented ||
          !event.currentTarget.contains(event.target as Node) ||
          (event.target as Element).closest('[role="dialog"]')
        )
          return
        if (event.key === 'Escape') {
          event.preventDefault()
          event.stopPropagation()
          back()
        }
      }}
    >
      <div className="fullscreen-platform-content" onKeyDown={platformVerticalFocus}>
        <h1>{page}</h1>
        {snapshot && (
          <>
            {!!counts?.[page.toLowerCase()] && (
              <p className="platform-title-count">
                {counts[page.toLowerCase()].toLocaleString('en-US')}{' '}
                {counts[page.toLowerCase()] === 1 ? 'game' : 'games'} in your library
              </p>
            )}
            {page === 'Steam' && (
              <SteamConnectionCard
                snapshot={snapshot}
                mode="fullscreen"
                titleCount={counts?.steam}
                tools={{ key: () => open('Steam Web API key'), purchase: () => open('Purchase history') }}
              />
            )}
            {page === 'Epic' && <EpicConnectionCard snapshot={snapshot} mode="fullscreen" />}
            {page === 'GOG' && (
              <section aria-label="GOG connection">
                <p
                  className="connection-state"
                  role="status"
                  aria-live="polite"
                  aria-label="Not needed"
                  data-tone="live"
                >
                  Not needed
                </p>
                <p>
                  Installed games and playtime are read from GOG Galaxy’s local files. There is nothing to
                  sign into here.
                </p>
              </section>
            )}
            {page === 'Steam Web API key' && (
              <SteamAccountOperation>
                <KeyTool snapshot={snapshot} />
              </SteamAccountOperation>
            )}
            {page === 'Purchase history' && (
              <SteamAccountOperation>
                <p>
                  Save your Steam account pages as HTML in your browser, or capture them in Winnow. Both
                  routes work without a saved session or API key.
                </p>
                <button onClick={() => open('Saved Steam pages')}>Read saved pages</button>
                <SteamCapture />
              </SteamAccountOperation>
            )}
            {page === 'Saved Steam pages' && (
              <SteamAccountOperation>
                <SteamPageImport mode="fullscreen" />
              </SteamAccountOperation>
            )}
          </>
        )}
        <Notice error={stores.error} />
        <button data-settings-child-back aria-label={`Back to ${parents[page]}`} onClick={back}>
          Back
        </button>
      </div>
      <PlatformHints />
    </section>
  )
}

function KeyTool({ snapshot }: { snapshot: StoreConnections }) {
  const command = useCommand()
  const busy = useSteamAccountBusy(command.isPending)
  return (
    <>
      <p>An API key never expires. Save it securely on this computer to add games never installed here.</p>
      <p role="status">{steamConnectionState(snapshot).keyState}</p>
      <SteamKeyForm hasKey={snapshot.steam.hasApiKey} />
      {snapshot.steam.apiKeyIsAppManaged && (
        <button
          disabled={busy}
          onClick={() => command.mutate({ route: 'connections.steam.key', body: { key: null } })}
        >
          Remove saved API key
        </button>
      )}
      <Notice error={command.error} />
    </>
  )
}

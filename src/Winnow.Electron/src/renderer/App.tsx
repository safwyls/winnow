import { useCallback, useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { MotionConfig } from 'motion/react'
import { AlertCircle, ArrowLeft, RotateCcw, WifiOff } from 'lucide-react'
import type { ApplicationActivation, ConnectionState } from '../shared/bridge'
import * as Dialog from '@radix-ui/react-dialog'
import type { ThemeContext, ThemePage } from '../shared/theme'
import { useLibrary, useFeed, useWorkspace, useApiQuery } from './api/hooks'
import { ApiError, primaryAction, request } from './api/client'
import { Details } from './features/Details'
import { Journal } from './features/Journal'
import { Settings } from './features/Settings'
import { Setup } from './features/Setup'
import type { SetupProgress } from './features/Setup'
import { SessionNotifications } from './features/SessionNotifications'
import { UpdateStatus } from './features/Updates'
import { LinkNotifications } from './features/LinkNotifications'
import { QuickMenu, OnScreenKeyboard } from './features/ControllerOverlays'
import { controllerScope, useController } from './controller'
import type { PresentationPreferenceValue } from './features/SettingsPreferences'
import { OfficialPluginInstall } from './features/SettingsPreferences'
import { avalon, AvalonShell, AvalonDiscover, AvalonLibrary } from './themes/avalon'
import { afterglow, AfterglowShell, AfterglowDiscover, AfterglowLibrary } from './themes/afterglow'
import { catalogue } from './themes/catalogue'
import { rift } from './themes/rift'
import { useThemeRuntime, ThemeBoundary, installThemeSDK } from './theming/runtime'
import { ThemeStudio } from './theming/ThemeStudio'
import { GameCard, Impression } from './components/primitives'
import { Artwork } from './components/Artwork'
import { GamePreview } from './components/GamePreview'
import { ArtworkEffects, ArtworkEffectsProvider } from './components/artwork-effects'
import { PortalSurface } from './components/portal-effects'
import { normalizeArtworkEffects } from '../shared/artworkEffects'
import { RefreshQueue, refreshJournalSnapshot, refreshSnapshots, shouldRefreshArtwork } from './refresh'
import { navigatePosition, returnFromSearch, type NavigationPosition } from './search-navigation'

installThemeSDK()
const builtins = [avalon, afterglow, rift, catalogue]
export function App() {
  const library = useLibrary(),
    feed = useFeed(),
    workspace = useWorkspace(),
    client = useQueryClient(),
    runtime = useThemeRuntime(builtins)
  const [connection, setConnection] = useState<ConnectionState>({
    connected: false,
    message: 'Connecting to your library…',
  })
  const [mode, setMode] = useState<'desktop' | 'fullscreen'>('desktop')
  const [positions, setPositions] = useState<Record<'desktop' | 'fullscreen', NavigationPosition>>({
    desktop: { page: 'discover', workId: null, previous: 'discover' },
    fullscreen: { page: 'discover', workId: null, previous: 'discover' },
  })
  const [notice, setNotice] = useState('')
  const [setupOpen, setSetupOpen] = useState(false)
  const setupProgress = useApiQuery<SetupProgress>('setup.get')
  const [quickMenu, setQuickMenu] = useState(false)
  const quickMenuAtRoot = useRef(true)
  const [keyboardInput, setKeyboardInput] = useState<HTMLInputElement | HTMLTextAreaElement | null>(null)
  const [activations, setActivations] = useState<ApplicationActivation[]>([])
  const [installRequest, setInstallRequest] = useState<Extract<
    ApplicationActivation,
    { kind: 'plugin' }
  > | null>(null)
  const handledActivations = useRef(new WeakSet<object>())
  useEffect(() => {
    let alive = true
    let pending = true
    const buffer: ApplicationActivation[] = []
    const receive = (value: ApplicationActivation) => {
      if (!alive) return
      if (pending) {
        if (buffer.length < 64) buffer.push(value)
        return
      }
      setActivations((items) => [...items, value].slice(0, 64))
    }
    const stop = window.winnow.onActivation?.(receive)
    void (window.winnow.takeActivations?.() ?? Promise.resolve([]))
      .then((values) => {
        pending = false
        if (alive) setActivations((items) => [...items, ...values, ...buffer].slice(0, 64))
      })
      .catch(() => {
        pending = false
        if (alive) setActivations((items) => [...items, ...buffer].slice(0, 64))
      })
    return () => {
      alive = false
      stop?.()
    }
  }, [])
  const presentation = useApiQuery<PresentationPreferenceValue[]>('preferences.presentation.get')
  const preferences = Object.fromEntries((presentation.data ?? []).map((row) => [row.preference, row.value]))
  const fullscreenMotion = mode === 'fullscreen' && preferences.FullscreenReducedMotion === 'true'
  const reducedMotion = runtime.profile.appearance.reducedMotion || fullscreenMotion
  useEffect(() => {
    const clamp = (value: string | null | undefined, fallback: number, min: number, max: number) => {
      const number = value === null || value === undefined ? fallback : Number(value)
      return Number.isFinite(number) ? Math.max(min, Math.min(max, number)) : fallback
    }
    const root = document.documentElement
    root.dataset.mode = mode
    root.style.setProperty(
      '--fullscreen-interface-scale',
      String(mode === 'fullscreen' ? clamp(preferences.FullscreenInterfaceScale, 1, 0.8, 1.2) : 1),
    )
    root.style.setProperty(
      '--fullscreen-text-scale',
      String(mode === 'fullscreen' ? clamp(preferences.FullscreenTextScale, 1, 0.7, 1.4) : 1),
    )
    root.style.setProperty(
      '--fullscreen-safe-margin',
      `${mode === 'fullscreen' ? clamp(preferences.FullscreenSafeMargin, 5, 0, 10) : 0}%`,
    )
    root.dataset.fitUltrawide = String(preferences.FullscreenFitUltrawide === 'true')
    root.dataset.dimDormant = String(preferences.DimDormantCovers !== 'false')
    root.classList.toggle('reduced-motion', reducedMotion)
  }, [
    mode,
    preferences.FullscreenInterfaceScale,
    preferences.FullscreenTextScale,
    preferences.FullscreenSafeMargin,
    preferences.FullscreenFitUltrawide,
    preferences.DimDormantCovers,
    reducedMotion,
  ])
  const launchAttempts = useRef(new Map<number, { operationId: string; action: string }>())
  const position = positions[mode]
  const detailOwners = useRef<Partial<Record<typeof mode, { workId: number; ids: number[] }>>>({})
  const navigate = useCallback(
    (page: ThemePage) => setPositions((all) => ({ ...all, [mode]: navigatePosition(all[mode], page) })),
    [mode],
  )
  const openGame = useCallback(
    (workId: number) => {
      const game = library.data?.games.find(
        (game) => game.workId === workId || game.entries.some((entry) => entry.workId === workId),
      )
      detailOwners.current[mode] = { workId, ids: game?.entries.map((entry) => entry.ownershipId) ?? [] }
      setPositions((all) => ({
        ...all,
        [mode]: {
          ...all[mode],
          workId,
          previous: all[mode].page === 'details' ? all[mode].previous : all[mode].page,
          page: 'details',
        },
      }))
    },
    [mode, library.data],
  )
  useEffect(() => {
    if (!library.data) return
    const games = library.data.games
    // Visibility is decided by the same published library used by tiles and counts.
    // Retain an open grouped game while one of its original ownerships remains visible.
    setPositions((all) => {
      let next = all
      for (const surface of ['desktop', 'fullscreen'] as const) {
        const current = all[surface]
        if (current.page !== 'details' || current.workId === null) continue
        const owner = detailOwners.current[surface]
        const ids = owner?.workId === current.workId ? owner.ids : []
        const visible = games.some((game) =>
          ids.length
            ? game.entries.some((entry) => ids.includes(entry.ownershipId))
            : game.workId === current.workId || game.entries.some((entry) => entry.workId === current.workId),
        )
        if (!visible) next = { ...next, [surface]: navigatePosition(current, current.previous) }
      }
      return next
    })
  }, [library.data])
  const closeGame = useCallback(() => navigate(position.previous), [navigate, position.previous])
  const closeSearch = useCallback(
    () => setPositions((all) => ({ ...all, [mode]: returnFromSearch(all[mode]) })),
    [mode],
  )
  const openSearch = useCallback(() => {
    if (mode === 'fullscreen' && runtime.theme.Search) navigate('search')
    else {
      navigate('library')
      setTimeout(() => document.querySelector<HTMLInputElement>('[data-library-search]')?.focus(), 50)
    }
  }, [mode, runtime.theme.Search, navigate])
  useEffect(() => {
    if (position.page === 'search' && !runtime.loading && !runtime.theme.Search) navigate('library')
  }, [position.page, runtime.loading, runtime.theme.Search, navigate])
  const toggleFullscreen = useCallback(() => {
    const next = mode === 'desktop' ? 'fullscreen' : 'desktop'
    void window.winnow
      .setFullscreen(next === 'fullscreen')
      .then(() => setMode(next))
      .catch(() => setNotice('The window could not change fullscreen mode.'))
  }, [mode])
  useEffect(() => {
    void window.winnow.isFullscreen().then((value) => setMode(value ? 'fullscreen' : 'desktop'))
    return window.winnow.onFullscreen((value) => setMode(value ? 'fullscreen' : 'desktop'))
  }, [])
  useEffect(() => {
    let active = true
    void window.winnow.connection().then((state) => {
      if (active) setConnection(state)
    })
    let refreshArtwork = false
    const queue = new RefreshQueue(() => {
      const artwork = refreshArtwork
      refreshArtwork = false
      return refreshSnapshots(client, { artwork })
    })
    const journalIds = new Set<number>()
    const journalQueue = new RefreshQueue(async () => {
      const ids = [...journalIds]
      journalIds.clear()
      await Promise.all(ids.map((id) => refreshJournalSnapshot(client, id)))
    })
    const stop = window.winnow.onEvent((event) => {
      const journal = /^sessions\/([1-9]\d*)\/journal$/.exec(event.resource ?? '')
      if (event.kind === 'library.changed' && journal && Number.isSafeInteger(Number(journal[1]))) {
        journalIds.add(Number(journal[1]))
        journalQueue.request()
        return
      }
      refreshArtwork ||= shouldRefreshArtwork(event)
      queue.request()
    })
    const stopConnection = window.winnow.onConnection((state) => {
      setConnection(state)
      if (state.connected) {
        refreshArtwork = true
        queue.request()
      }
    })
    return () => {
      active = false
      stop()
      stopConnection()
      queue.dispose()
      journalQueue.dispose()
    }
  }, [client])
  useEffect(() => {
    const key = (event: KeyboardEvent) => {
      if (event.defaultPrevented) return
      if (setupOpen && event.key !== 'F11') return
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        if (controllerScope() !== document) return
        event.preventDefault()
        openSearch()
      }
      if (event.key === 'F11') {
        event.preventDefault()
        toggleFullscreen()
      }
      if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === 't') {
        event.preventDefault()
        runtime.resetProfile()
        navigate('studio')
      }
      if (event.key === 'Escape' && controllerScope() === document) {
        if (position.page === 'details') navigate(position.previous)
        else if (position.page === 'search') closeSearch()
        else if (mode === 'fullscreen') toggleFullscreen()
      }
    }
    window.addEventListener('keydown', key)
    return () => window.removeEventListener('keydown', key)
  }, [mode, position, navigate, openSearch, closeSearch, toggleFullscreen, runtime.resetProfile, setupOpen])
  useEffect(() => {
    document.title = `Winnow · ${runtime.theme.name}`
  }, [runtime.theme.name])
  const lastNavigation = useRef<{ page: ThemePage; mode: string; themeId: string } | null>(null)
  useEffect(() => {
    const previous = lastNavigation.current
    const portalJourney =
      runtime.theme.id === 'rift' &&
      previous?.themeId === 'rift' &&
      previous.mode === mode &&
      (previous.page === 'details' || position.page === 'details')
    const retainedDetails =
      runtime.theme.id === 'avalon' &&
      previous?.themeId === 'avalon' &&
      mode === 'desktop' &&
      previous.mode === mode &&
      (previous.page === 'details' || position.page === 'details')
    if (previous && !portalJourney && !retainedDetails) {
      document.getElementById('main-content')?.focus({ preventScroll: true })
      const content = document.getElementById('main-content')
      if (content) content.scrollTop = 0
    }
    lastNavigation.current = { page: position.page, mode, themeId: runtime.theme.id }
  }, [position.page, position.workId, mode, runtime.theme.id])
  useEffect(() => {
    setKeyboardInput(null)
    setQuickMenu(false)
  }, [mode, position.page])
  useController({
    enabled: mode === 'fullscreen',
    menu: () => {
      if (setupOpen || keyboardInput || quickMenu) return
      if (mode !== 'fullscreen') {
        toggleFullscreen()
        return
      }
      quickMenuAtRoot.current =
        controllerScope() === document &&
        ['discover', 'library', 'journal', 'settings'].includes(position.page)
      setQuickMenu(true)
    },
    search: () => {
      if (setupOpen) return
      openSearch()
    },
    switchPage: (delta) => {
      if (setupOpen) return
      const pages: ThemePage[] = ['discover', 'library', 'journal', 'settings']
      const index = pages.indexOf(position.page)
      if (index >= 0) navigate(pages[(index + delta + pages.length) % pages.length])
    },
    keyboard: setKeyboardInput,
    play: () => {
      if (controllerScope() !== document) return
      const workId = Number(
        document.activeElement?.closest('[data-work-id]')?.getAttribute('data-work-id') ?? position.workId,
      )
      const game = library.data?.games.find((game) => game.workId === workId)
      const entry = game?.entries.find((entry) => entry.installed && primaryAction(entry, workspace.data))
      if (entry)
        void context.actions
          .launch(entry.ownershipId)
          .catch((error) =>
            setNotice(error instanceof Error ? error.message : 'The game could not be started.'),
          )
    },
  })
  let context: ThemeContext
  const renderScreen = (page: ThemePage = position.page) => {
    if (page === 'studio') return <ThemeStudio runtime={runtime} />
    if (page === 'discover') return <AvalonDiscover {...context} />
    if (page === 'library' || page === 'search') return <AvalonLibrary {...context} />
    if (page === 'journal') return <Journal mode={mode} onOpenGame={openGame} />
    if (page === 'settings') return <Settings mode={mode} />
    return position.workId !== null ? (
      <Details workId={position.workId} mode={mode} onClose={closeGame} />
    ) : (
      <p>Choose a game from your library.</p>
    )
  }
  context = {
    mode,
    page: position.page,
    selectedWorkId: position.workId,
    setPage: navigate,
    openGame,
    closeGame,
    closeSearch,
    openSearch,
    editText: setKeyboardInput,
    previousPage: position.previous,
    toggleFullscreen,
    games: library.data?.games ?? [],
    feed: feed.data,
    feedLoading: feed.isPending,
    feedFailed: feed.isError || feed.data?.failed,
    loading: library.isPending,
    profileHydrated: !runtime.loading,
    profile: fullscreenMotion
      ? { ...runtime.profile, appearance: { ...runtime.profile.appearance, reducedMotion: true } }
      : runtime.profile,
    children: null,
    renderScreen,
    actions: {
      launch: async (ownershipId) => {
        const entry = library.data?.games
          .flatMap((game) => game.entries)
          .find((entry) => entry.ownershipId === ownershipId)
        const action = entry ? primaryAction(entry, workspace.data) : null
        if (!action) throw Error('No supported launch action is available for this copy.')
        const operation = launchAttempts.current.get(ownershipId) ?? {
          operationId: crypto.randomUUID(),
          action,
        }
        launchAttempts.current.set(ownershipId, operation)
        try {
          const result = await request<number>('actions.execute', { ownershipId }, operation)
          launchAttempts.current.delete(ownershipId)
          if (result === 2) throw Error('The launcher could not accept this action.')
        } catch (error) {
          if (!(error instanceof ApiError) || !error.uncertain) launchAttempts.current.delete(ownershipId)
          throw error
        }
      },
    },
    components: { GameCard, Impression, Artwork, ArtworkEffects, GamePreview, PortalSurface },
  }
  const activationContext = useRef(context)
  activationContext.current = context
  useEffect(() => {
    const activation = activations[0]
    if (
      !activation ||
      setupProgress.isPending ||
      setupProgress.isError ||
      (activation.kind !== 'plugin' && (typeof setupProgress.data?.step === 'number' || setupOpen)) ||
      installRequest ||
      (activation.kind === 'game' && (library.isPending || !workspace.data))
    )
      return
    setActivations((items) => items.slice(1))
    if (handledActivations.current.has(activation)) return
    handledActivations.current.add(activation)
    if (activation.kind === 'fullscreen') {
      if (mode !== 'fullscreen')
        void window.winnow
          .setFullscreen(true)
          .then(() => setMode('fullscreen'))
          .catch(() => setNotice('Fullscreen could not be opened.'))
    } else if (activation.kind === 'game') {
      void activationContext.current.actions
        .launch(activation.ownershipId)
        .catch((error) =>
          setNotice(error instanceof Error ? error.message : 'This game could not be started.'),
        )
    } else if (activation.kind === 'plugin') setInstallRequest(activation)
  }, [
    activations,
    setupProgress.isPending,
    setupProgress.isError,
    setupProgress.data?.step,
    setupOpen,
    installRequest,
    library.isPending,
    workspace.data,
    mode,
  ])
  const screenNames = {
    discover: 'Discover',
    library: 'Library',
    search: 'Search',
    details: 'Details',
    journal: 'Journal',
    settings: 'Settings',
    studio: null,
  } as const
  const name = screenNames[position.page],
    Screen = name ? runtime.theme[name] : undefined
  const content = (
    <div className={`screen-view screen-${position.page}`} key={`${mode}-${position.page}`}>
      {Screen ? <Screen {...context} /> : renderScreen()}
    </div>
  )
  const Shell = runtime.theme.Shell ?? AvalonShell
  return (
    <MotionConfig reducedMotion={reducedMotion ? 'always' : 'user'}>
      <ArtworkEffectsProvider
        options={normalizeArtworkEffects(runtime.profile.appearance.artwork)}
        reducedMotion={reducedMotion}
      >
        <a className="skip-link" href="#main-content">
          Skip to content
        </a>
        <div className="host-status">
          {!connection.connected && (
            <div className="connection-banner" role="status">
              <WifiOff size={16} />
              <span>{connection.message}</span>
              <button onClick={() => void client.invalidateQueries()}>Retry</button>
            </div>
          )}
          {library.isError && (
            <div className="error-banner" role="alert">
              <AlertCircle size={16} />
              {library.error.message}
              <button onClick={() => void library.refetch()}>Try again</button>
            </div>
          )}
          {feed.isError && position.page === 'discover' && (
            <div className="error-banner" role="alert">
              <AlertCircle size={16} />
              Recommendations could not be loaded. {feed.error.message}
              <button onClick={() => void feed.refetch()}>Retry recommendations</button>
            </div>
          )}
          {(notice || runtime.notice) && (
            <div className="status-banner" role="status">
              {notice || runtime.notice}
            </div>
          )}
        </div>
        <div className="theme-viewport">
          <ThemeBoundary
            resetKey={runtime.theme.id}
            onError={runtime.recoverTheme}
            fallback={
              <div className="theme-recovery">
                <h1>Let’s get you back.</h1>
                <p>The selected theme could not display this screen.</p>
                <button
                  className="primary"
                  onClick={() => {
                    runtime.resetProfile()
                    setNotice('')
                    navigate('discover')
                  }}
                >
                  <RotateCcw size={18} />
                  Restore Avalon
                </button>
              </div>
            }
          >
            <Shell {...context}>{content}</Shell>
          </ThemeBoundary>
        </div>
        <Setup
          mode={mode}
          suspended={!!installRequest}
          onOpenChange={setSetupOpen}
          appearance={
            <label className="field">
              Winnow design
              <select
                value={runtime.profile.themeId}
                onChange={(event) => runtime.selectTheme(event.target.value)}
              >
                {runtime.builtins.map((theme) => (
                  <option key={theme.id} value={theme.id}>
                    {theme.name}
                  </option>
                ))}
              </select>
              <small>You can adjust colors and typography in Theme Studio.</small>
            </label>
          }
        />
        <SessionNotifications mode={mode} suspended={setupOpen} />
        {!setupOpen && <UpdateStatus />}
        <LinkNotifications />
        <Dialog.Root
          open={!!installRequest}
          onOpenChange={(open) => {
            if (!open) setInstallRequest(null)
          }}
        >
          <Dialog.Portal>
            <Dialog.Overlay className="dialog-overlay" />
            <Dialog.Content className={`dialog-content mode-${mode}`}>
              <Dialog.Title>Review provider installation</Dialog.Title>
              <Dialog.Description>
                The link selected this official provider. Review it before installing code that runs on this
                computer.
              </Dialog.Description>
              {installRequest && (
                <OfficialPluginInstall
                  key={`${installRequest.pluginId}:${installRequest.releaseTag}`}
                  initialRequest={installRequest}
                />
              )}
              <Dialog.Close asChild>
                <button>Close</button>
              </Dialog.Close>
            </Dialog.Content>
          </Dialog.Portal>
        </Dialog.Root>
        {quickMenu && (
          <QuickMenu
            atRoot={quickMenuAtRoot.current}
            close={() => setQuickMenu(false)}
            navigate={navigate}
            exit={toggleFullscreen}
          />
        )}
        {keyboardInput && <OnScreenKeyboard input={keyboardInput} close={() => setKeyboardInput(null)} />}
        <button
          className="recovery-shortcut"
          title="Restore default theme (Ctrl+Shift+T)"
          aria-label="Restore default theme"
          onClick={() => {
            runtime.resetProfile()
            navigate('studio')
          }}
        >
          <RotateCcw size={14} />
        </button>
      </ArtworkEffectsProvider>
    </MotionConfig>
  )
}

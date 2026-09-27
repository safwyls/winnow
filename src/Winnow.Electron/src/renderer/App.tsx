import { useCallback, useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { MotionConfig } from 'motion/react'
import { AlertCircle, ArrowLeft, RotateCcw, WifiOff } from 'lucide-react'
import type { ConnectionState } from '../shared/bridge'
import type { ThemeContext, ThemePage } from '../shared/theme'
import { useLibrary, useFeed, useWorkspace } from './api/hooks'
import { ApiError, primaryAction, request } from './api/client'
import { Details } from './features/Details'
import { Journal } from './features/Journal'
import { Settings } from './features/Settings'
import { afterglow, AfterglowShell, AfterglowDiscover, AfterglowLibrary } from './themes/afterglow'
import { catalogue } from './themes/catalogue'
import { useThemeRuntime, ThemeBoundary, installThemeSDK } from './theming/runtime'
import { ThemeStudio } from './theming/ThemeStudio'
import { GameCard, Impression } from './components/primitives'
import { Artwork } from './components/Artwork'
import { RefreshQueue, refreshSnapshots } from './refresh'

installThemeSDK()
const builtins = [afterglow, catalogue]
interface Position {
  page: ThemePage
  workId: number | null
  previous: ThemePage
}
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
  const [positions, setPositions] = useState<Record<'desktop' | 'fullscreen', Position>>({
    desktop: { page: 'discover', workId: null, previous: 'discover' },
    fullscreen: { page: 'discover', workId: null, previous: 'discover' },
  })
  const [notice, setNotice] = useState('')
  const launchAttempts = useRef(new Map<number, { operationId: string; action: string }>())
  const position = positions[mode]
  const navigate = useCallback(
    (page: ThemePage) =>
      setPositions((all) => ({ ...all, [mode]: { ...all[mode], previous: all[mode].page, page } })),
    [mode],
  )
  const openGame = useCallback(
    (workId: number) =>
      setPositions((all) => ({
        ...all,
        [mode]: {
          ...all[mode],
          workId,
          previous: all[mode].page === 'details' ? all[mode].previous : all[mode].page,
          page: 'details',
        },
      })),
    [mode],
  )
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
    const queue = new RefreshQueue(() => refreshSnapshots(client))
    const stop = window.winnow.onEvent(() => queue.request())
    const stopConnection = window.winnow.onConnection((state) => {
      setConnection(state)
      if (state.connected) queue.request()
    })
    return () => {
      active = false
      stop()
      stopConnection()
      queue.dispose()
    }
  }, [client])
  useEffect(() => {
    const key = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        navigate('library')
        setTimeout(() => document.querySelector<HTMLInputElement>('[data-library-search]')?.focus(), 50)
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
      if (event.key === 'Escape' && !document.querySelector('[role="dialog"]')) {
        if (position.page === 'details') navigate(position.previous)
        else if (mode === 'fullscreen') toggleFullscreen()
      }
    }
    window.addEventListener('keydown', key)
    return () => window.removeEventListener('keydown', key)
  }, [mode, position, navigate, toggleFullscreen, runtime.resetProfile])
  useEffect(() => {
    document.title = `Winnow · ${runtime.theme.name}`
  }, [runtime.theme.name])
  const didNavigate = useRef(false)
  useEffect(() => {
    if (didNavigate.current) {
      document.getElementById('main-content')?.focus({ preventScroll: true })
      document.documentElement.scrollTop = 0
    }
    didNavigate.current = true
  }, [position.page, mode])
  useGamepad(mode === 'fullscreen')
  let context: ThemeContext
  const renderScreen = (page: ThemePage = position.page) => {
    if (page === 'studio') return <ThemeStudio runtime={runtime} />
    if (page === 'discover') return <AfterglowDiscover {...context} />
    if (page === 'library') return <AfterglowLibrary {...context} />
    if (page === 'journal') return <Journal mode={mode} onOpenGame={openGame} />
    if (page === 'settings') return <Settings mode={mode} />
    return position.workId !== null ? (
      <Details workId={position.workId} mode={mode} onClose={() => navigate(position.previous)} />
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
    toggleFullscreen,
    games: library.data?.games ?? [],
    feed: feed.data,
    loading: library.isPending,
    profile: runtime.profile,
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
    components: { GameCard, Impression, Artwork },
  }
  const screenNames = {
    discover: 'Discover',
    library: 'Library',
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
  const Shell = runtime.theme.Shell ?? AfterglowShell
  return (
    <MotionConfig reducedMotion={runtime.profile.appearance.reducedMotion ? 'always' : 'user'}>
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
              Restore Afterglow
            </button>
          </div>
        }
      >
        <Shell {...context}>{content}</Shell>
      </ThemeBoundary>
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
    </MotionConfig>
  )
}

function useGamepad(enabled: boolean) {
  useEffect(() => {
    if (!enabled) return
    let frame = 0,
      last = 0,
      previousButtons: boolean[] = []
    const move = (direction: number) => {
      const candidates = [
        ...document.querySelectorAll<HTMLElement>(
          'button:not(:disabled), input, select, textarea, [tabindex="0"]',
        ),
      ].filter(
        (element) => element.getBoundingClientRect().width > 0 && element.getBoundingClientRect().height > 0,
      )
      const index = candidates.indexOf(document.activeElement as HTMLElement)
      candidates[(index + direction + candidates.length) % candidates.length]?.focus()
    }
    const tick = (time: number) => {
      const pad = navigator.getGamepads?.().find(Boolean)
      if (pad) {
        const buttons = pad.buttons.map((button) => button.pressed)
        if (buttons[0] && !previousButtons[0]) (document.activeElement as HTMLElement)?.click()
        if (buttons[1] && !previousButtons[1])
          window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
        const axis = pad.axes[1] ?? 0
        if (time - last > 180) {
          if (buttons[13] || buttons[15] || axis > 0.5) {
            move(1)
            last = time
          } else if (buttons[12] || buttons[14] || axis < -0.5) {
            move(-1)
            last = time
          }
        }
        previousButtons = buttons
      }
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [enabled])
}

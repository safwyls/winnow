import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { BookOpen, Compass, Expand, Grid2X2, Palette, Search, Settings2, X } from 'lucide-react'
import { themeSettingValues, type ThemeContext, type ThemeDefinition } from '../../shared/theme'
import { useSystemReducedMotion } from '../useSystemReducedMotion'
import dragon from '../assets/dragon.svg'
import { JourneyContext, type JourneyOrigin } from './rift/journey'
import { Starfield } from './rift/Starfield'
import { riftSettings } from './rift/settings'
import { RiftDiscover } from './rift/Discover'
import { RiftLibrary } from './rift/Library'
import { RiftDetails } from './rift/Details'
import './rift/rift.css'

export function RiftShell(context: ThemeContext) {
  const origins = useRef({
    desktop: { current: null as JourneyOrigin | null },
    fullscreen: { current: null as JourneyOrigin | null },
  })
  const origin = origins.current[context.mode]
  if (
    context.page === 'details' &&
    (origin.current?.workId !== context.selectedWorkId || origin.current?.page !== context.previousPage)
  )
    origin.current = null
  const retainedBrowse = useRef({ desktop: context.children, fullscreen: context.children })
  const [journeyExpanded, setJourneyExpanded] = useState({ desktop: false, fullscreen: false })
  const finishJourney = useCallback(
    () => setJourneyExpanded((value) => ({ ...value, [context.mode]: true })),
    [context.mode],
  )
  if (context.page === 'discover' || context.page === 'library')
    retainedBrowse.current[context.mode] = context.children
  const previous = useRef({ desktop: context.page, fullscreen: context.page })
  const settings = themeSettingValues(rift, context.profile)
  const systemMotion = useSystemReducedMotion()
  const reducedMotion = systemMotion || context.profile.appearance.reducedMotion
  const options = useMemo(
    () => ({
      roundness: Number(settings.portalRoundness),
      waviness: Number(settings.portalWaviness),
      activity: Number(settings.portalActivity),
    }),
    [settings.portalRoundness, settings.portalWaviness, settings.portalActivity],
  )
  useEffect(() => {
    const restoring = previous.current[context.mode] === 'details'
    previous.current[context.mode] = context.page
    if (!restoring) return
    let frame = 0
    const focus = () => {
      const target =
        origin.current?.page === context.page
          ? document.querySelector<HTMLElement>(
              `.rift-gallery [data-rift-game="${origin.current?.workId}"], .rift-deck-selected[data-rift-game="${origin.current?.workId}"]`,
            )
          : document.getElementById('main-content')
      if (target) {
        target.focus({ preventScroll: true })
        observer.disconnect()
      }
    }
    // Virtual rows mount after their viewport is measured.
    const observer = new MutationObserver(() => {
      cancelAnimationFrame(frame)
      frame = requestAnimationFrame(focus)
    })
    observer.observe(document.getElementById('main-content')!, { childList: true, subtree: true })
    frame = requestAnimationFrame(focus)
    const timer = setTimeout(() => {
      observer.disconnect()
      if (!document.activeElement || document.activeElement === document.body)
        document.getElementById('main-content')?.focus({ preventScroll: true })
    }, 1000)
    return () => {
      cancelAnimationFrame(frame)
      clearTimeout(timer)
      observer.disconnect()
    }
  }, [context.page, context.mode])
  const journey = {
    origin,
    options,
    reducedMotion,
    coverSize: String(settings.coverSize),
    finish: finishJourney,
    open(workId: number, source?: HTMLElement | DOMRect | null) {
      origin.current = {
        workId,
        page: context.page,
        rect: source instanceof HTMLElement ? source.getBoundingClientRect() : (source ?? null),
      }
      setJourneyExpanded((value) => ({ ...value, [context.mode]: false }))
      context.openGame(workId)
    },
  }
  return (
    <JourneyContext.Provider value={journey}>
      <div className={`rift-shell ${context.mode}`} data-page={context.page} data-size={settings.coverSize}>
        {settings.stars && <Starfield brightness={Number(settings.starBrightness)} />}
        <header className="rift-bar">
          <button className="rift-brand" aria-label="Winnow home" onClick={() => context.setPage('discover')}>
            <span className="dragon-mark" style={{ maskImage: `url(${dragon})` }} />
            <strong>winnow</strong>
            <span>RIFT</span>
          </button>
          <nav aria-label="Main navigation" className="rift-navigation">
            {[
              { id: 'discover', label: 'Discover', Icon: Compass },
              { id: 'library', label: 'Library', Icon: Grid2X2 },
              { id: 'journal', label: 'Journal', Icon: BookOpen },
            ].map(({ id, label, Icon }) => (
              <button
                key={id}
                aria-current={context.page === id ? 'page' : undefined}
                aria-label={label}
                onClick={() => context.setPage(id as 'discover' | 'library' | 'journal')}
              >
                <Icon size={16} />
                <span>{label}</span>
              </button>
            ))}
          </nav>
          <div className="rift-utilities">
            <button
              title="Search library (Ctrl+K)"
              aria-label="Search library"
              onClick={() => {
                context.setPage('library')
                setTimeout(
                  () => document.querySelector<HTMLInputElement>('[data-library-search]')?.focus(),
                  50,
                )
              }}
            >
              <Search size={17} />
            </button>
            <button title="Theme Studio" aria-label="Theme Studio" onClick={() => context.setPage('studio')}>
              <Palette size={17} />
            </button>
            <button title="Settings" aria-label="Settings" onClick={() => context.setPage('settings')}>
              <Settings2 size={17} />
            </button>
            <button
              title="Fullscreen (F11)"
              aria-label={context.mode === 'fullscreen' ? 'Leave fullscreen' : 'Enter fullscreen'}
              onClick={context.toggleFullscreen}
            >
              {context.mode === 'fullscreen' ? <X size={17} /> : <Expand size={17} />}
            </button>
          </div>
        </header>
        <main id="main-content" tabIndex={-1} className="rift-content">
          {context.page === 'details' && origin.current && !journeyExpanded[context.mode] && (
            <div className="rift-journey-underlay" inert aria-hidden="true">
              {retainedBrowse.current[context.mode]}
            </div>
          )}
          {context.children}
        </main>
        <footer className="rift-footer">
          <span>
            <i /> YOUR LIBRARY, ANOTHER DIMENSION.
          </span>
          <span>
            {context.games.length.toLocaleString()} games <b>·</b>{' '}
            {context.mode === 'fullscreen'
              ? 'Arrows to explore · Enter to view · Esc to go back'
              : 'Ctrl+K to find a world'}
          </span>
        </footer>
      </div>
    </JourneyContext.Provider>
  )
}

export const rift: ThemeDefinition = {
  apiVersion: 1,
  id: 'rift',
  name: 'Rift',
  Shell: RiftShell,
  Discover: RiftDiscover,
  Library: RiftLibrary,
  Details: RiftDetails,
  settings: riftSettings,
  defaults: {
    appearance: {
      palette: 'rift',
      accent: '#a1e6d3',
      font: 'modern',
      density: 'comfortable',
      radius: 12,
      scrim: 55,
    },
    layout: { navigation: 'left', cardStyle: 'poster' },
  },
}

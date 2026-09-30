import { useLayoutEffect, useRef, useSyncExternalStore, type ReactNode, type RefObject } from 'react'
import { AvalonRowMotion, desktopShelfWidth, homeRowLayout } from './avalon-row-motion'

export function useHomeRowGeometry(
  root: RefObject<HTMLDivElement | null>,
  enabled: boolean,
  onCapacity: (capacity: number) => void,
) {
  useLayoutEffect(() => {
    const element = root.current
    if (!enabled || !element) return
    const hero = element.querySelector<HTMLElement>('.avalon-home-hero')!,
      shelf = element.querySelector<HTMLElement>('.avalon-home-shelf')!,
      heading = shelf.querySelector('h2')!,
      shell = element.closest<HTMLElement>('.avalon-shell')!
    const resize = () => {
      if (!element.clientWidth) return
      const zoom = element.getBoundingClientRect().width / element.clientWidth
      if (zoom <= 0) return
      const headingStyle = getComputedStyle(heading),
        shelfStyle = getComputedStyle(shelf)
      const available =
        element.clientHeight -
        hero.offsetHeight -
        heading.offsetHeight -
        parseFloat(headingStyle.marginTop) -
        parseFloat(headingStyle.marginBottom) -
        parseFloat(shelfStyle.paddingTop)
      const canvasHeight =
        innerHeight -
        (parseFloat(getComputedStyle(shell).paddingTop) + parseFloat(getComputedStyle(shell).paddingBottom)) *
          zoom
      // Cover targets use reference pixels; display resolution must not enlarge them a second time.
      const viewportScale = Math.max(1, Math.min(innerHeight / 1080, innerWidth / 1920))
      const { height, width, capacity } = homeRowLayout(
        element.clientWidth,
        available,
        canvasHeight / viewportScale,
        zoom / viewportScale,
      )
      const value = `${height}px`
      if (element.style.getPropertyValue('--home-row-height') !== value)
        element.style.setProperty('--home-row-height', value)
      const rowWidth = `${width}px`
      if (element.style.getPropertyValue('--home-row-width') !== rowWidth)
        element.style.setProperty('--home-row-width', rowWidth)
      onCapacity(capacity)
    }
    resize()
    const observer = new ResizeObserver(resize)
    for (const observed of [element, hero, heading]) observer.observe(observed)
    const preferences = new MutationObserver(resize)
    preferences.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['style', 'data-fit-ultrawide'],
    })
    window.addEventListener('resize', resize)
    return () => {
      observer.disconnect()
      preferences.disconnect()
      window.removeEventListener('resize', resize)
    }
  }, [root, enabled, onCapacity])
}

export function AvalonDesktopShelf({ id, children }: { id: string; children: ReactNode }) {
  const root = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const element = root.current!
    const resize = () =>
      element.style.setProperty('--desktop-shelf-width', `${desktopShelfWidth(element.clientWidth)}px`)
    resize()
    const observer = new ResizeObserver(resize)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])
  return (
    <div ref={root} className="avalon-desktop-covers" data-shelf-id={id}>
      {children}
    </div>
  )
}

export function AvalonHomeRow({
  id,
  page,
  children,
  onFocusChange,
}: {
  id: string
  page: number
  children: ReactNode
  onFocusChange(value: boolean): void
}) {
  return (
    <div
      className="avalon-home-row"
      data-shelf-id={id}
      data-home-page={page}
      onFocusCapture={() => onFocusChange(true)}
      onBlurCapture={(event) => {
        if (event.relatedTarget instanceof Node && !event.currentTarget.contains(event.relatedTarget))
          onFocusChange(false)
      }}
    >
      {children}
    </div>
  )
}

export function AvalonRowViewport<T extends { id: string }>({
  rows,
  first,
  reducedMotion,
  children,
  viewportRef,
  onReady,
  visible = 1,
}: {
  rows: T[]
  first: number
  reducedMotion: boolean
  children(row: T, index: number): ReactNode
  viewportRef?: RefObject<HTMLDivElement | null>
  onReady?(): void
  visible?: number
}) {
  const local = useRef<HTMLDivElement>(null),
    root = viewportRef ?? local
  const motion = useRef<AvalonRowMotion | null>(null)
  if (!motion.current) {
    motion.current = new AvalonRowMotion((frame) => requestAnimationFrame(frame))
    motion.current.configure(rows.length, visible, first)
  }
  const model = motion.current,
    state = useSyncExternalStore(model.subscribe, model.snapshot)
  const ids = rows.map((row) => row.id),
    previous = useRef(ids),
    previousVisible = useRef(visible),
    ready = useRef(onReady)
  ready.current = onReady
  useLayoutEffect(() => {
    model.attach()
    const element = root.current!
    const resize = () => model.resize(element.clientWidth, element.clientHeight)
    resize()
    const observer = new ResizeObserver(resize)
    observer.observe(element)
    return () => {
      observer.disconnect()
      model.detach()
    }
  }, [model])
  useLayoutEffect(() => {
    const old = previous.current
    const same = old.length === ids.length && old.every((id, index) => id === ids[index])
    if (!same || previousVisible.current !== visible) {
      if (
        previousVisible.current === visible &&
        ids.length > old.length &&
        old.every((id, index) => id === ids[index])
      )
        model.append(ids.length)
      else model.configure(ids.length, visible, first)
      previous.current = ids
      previousVisible.current = visible
    }
    model.reduce(reducedMotion)
    model.show(first)
  }, [ids.join('\0'), first, reducedMotion, visible, model])
  useLayoutEffect(() => {
    ready.current?.()
  }, [state.first, state.realized.join(',')])
  return (
    <div
      className="avalon-row-viewport"
      data-animating={state.animating || undefined}
      data-first-row={state.first}
      ref={root}
    >
      {state.realized.map(
        (index) =>
          rows[index] && (
            <div
              className="avalon-retained-row"
              key={rows[index].id}
              data-row-id={rows[index].id}
              data-row-active={index >= first && index < first + visible}
              aria-hidden={index < first || index >= first + visible}
              inert={index < first || index >= first + visible}
              style={{
                transform: `translateY(${(index - state.offset) * 100}%)`,
                height: `${100 / Math.max(1, visible)}%`,
                pointerEvents: index >= first && index < first + visible ? 'auto' : 'none',
              }}
            >
              {children(rows[index], index)}
            </div>
          ),
      )}
    </div>
  )
}

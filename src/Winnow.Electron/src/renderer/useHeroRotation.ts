import { useCallback, useEffect, useRef, useState, type FocusEventHandler, type RefCallback } from 'react'

interface HeroRotationOptions {
  count: number
  index: number
  onSelect(index: number): void
  reducedMotion: boolean
  enabled: boolean
}

export interface HeroRotation {
  ref: RefCallback<HTMLElement>
  /** Explicit user pause; pointer, focus, and visibility pauses are reflected by rotating. */
  paused: boolean
  rotating: boolean
  togglePaused(): void
  pause(): void
  /** A deliberate selection also pauses autoplay until the user chooses Resume. */
  select(index: number): void
  onMouseEnter(): void
  onMouseLeave(): void
  onFocusCapture: FocusEventHandler<HTMLElement>
  onBlurCapture: FocusEventHandler<HTMLElement>
}

const rotationDelay = 9_000

export function useHeroRotation({
  count,
  index,
  onSelect,
  reducedMotion,
  enabled,
}: HeroRotationOptions): HeroRotation {
  const [element, setElement] = useState<HTMLElement | null>(null)
  const [paused, setPaused] = useState(false)
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const [inViewport, setInViewport] = useState(false)
  const [foreground, setForeground] = useState(
    () => document.visibilityState === 'visible' && document.hasFocus(),
  )
  const available = Number.isFinite(count) ? Math.max(0, Math.floor(count)) : 0
  const currentIndex = Number.isFinite(index)
    ? Math.min(Math.max(0, Math.floor(index)), Math.max(0, available - 1))
    : 0
  const selectRef = useRef(onSelect)
  selectRef.current = onSelect

  const ref = useCallback<RefCallback<HTMLElement>>((node) => {
    setElement(node)
    setInViewport(false)
    setHovered(node?.matches(':hover') ?? false)
    setFocused(!!node && node.contains(document.activeElement))
  }, [])

  useEffect(() => {
    if (!element || typeof IntersectionObserver === 'undefined') return
    let active = true
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (active && entry) setInViewport(entry.isIntersecting && entry.intersectionRatio >= 0.5)
      },
      { threshold: [0, 0.5] },
    )
    observer.observe(element)
    return () => {
      active = false
      observer.disconnect()
    }
  }, [element])

  useEffect(() => {
    const synchronize = () => setForeground(document.visibilityState === 'visible' && document.hasFocus())
    const blur = () => setForeground(false)
    window.addEventListener('focus', synchronize)
    window.addEventListener('blur', blur)
    document.addEventListener('visibilitychange', synchronize)
    return () => {
      window.removeEventListener('focus', synchronize)
      window.removeEventListener('blur', blur)
      document.removeEventListener('visibilitychange', synchronize)
    }
  }, [])

  const rotating =
    available > 1 && enabled && !reducedMotion && !paused && !hovered && !focused && inViewport && foreground
  useEffect(() => {
    if (!rotating) return
    // Each resumed view gets a full reading interval. Background time never accumulates advances.
    const timer = window.setTimeout(() => {
      if (document.visibilityState === 'visible' && document.hasFocus())
        selectRef.current((currentIndex + 1) % available)
    }, rotationDelay)
    return () => window.clearTimeout(timer)
  }, [rotating, available, currentIndex])

  const pause = useCallback(() => setPaused(true), [])
  const togglePaused = useCallback(() => setPaused((value) => !value), [])
  const select = useCallback(
    (nextIndex: number) => {
      setPaused(true)
      if (available > 0 && Number.isFinite(nextIndex))
        selectRef.current(((Math.trunc(nextIndex) % available) + available) % available)
    },
    [available],
  )
  const onMouseEnter = useCallback(() => setHovered(true), [])
  const onMouseLeave = useCallback(() => setHovered(false), [])
  const onFocusCapture = useCallback<FocusEventHandler<HTMLElement>>(() => setFocused(true), [])
  const onBlurCapture = useCallback<FocusEventHandler<HTMLElement>>((event) => {
    if (!(event.relatedTarget instanceof Node) || !event.currentTarget.contains(event.relatedTarget))
      setFocused(false)
  }, [])

  return {
    ref,
    paused,
    rotating,
    togglePaused,
    pause,
    select,
    onMouseEnter,
    onMouseLeave,
    onFocusCapture,
    onBlurCapture,
  }
}

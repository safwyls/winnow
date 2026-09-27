import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
  type RefObject,
} from 'react'
import {
  DEFAULT_ARTWORK_EFFECTS,
  normalizeArtworkEffects,
  type ArtworkEffectOptions,
} from '../../../shared/artworkEffects'
import { artworkEffects } from './interaction'
import './artwork-effects.css'

export type { ArtworkEffectOptions } from '../../../shared/artworkEffects'

const EffectContext = createContext({ options: DEFAULT_ARTWORK_EFFECTS, reducedMotion: false })

/** Share persisted presentation defaults without coupling effects to any theme or API. */
export function ArtworkEffectsProvider({
  options,
  reducedMotion,
  children,
}: {
  options: ArtworkEffectOptions
  reducedMotion: boolean
  children: ReactNode
}) {
  const [systemMotion, setSystemMotion] = useState(
    () => typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches,
  )
  useEffect(() => {
    if (typeof matchMedia !== 'function') return
    const media = matchMedia('(prefers-reduced-motion: reduce)')
    const change = () => setSystemMotion(media.matches)
    change()
    media.addEventListener('change', change)
    return () => media.removeEventListener('change', change)
  }, [])
  const value = useMemo(
    () => ({ options, reducedMotion: reducedMotion || systemMotion }),
    [options, reducedMotion, systemMotion],
  )
  return <EffectContext.Provider value={value}>{children}</EffectContext.Provider>
}

/** Wrap one centered, cover-fit image. interactionRef must point to an untransformed hit area. */
export function ArtworkEffects({
  children,
  className = '',
  effects,
  interactionRef,
}: {
  children: ReactNode
  className?: string
  effects?: Partial<ArtworkEffectOptions> | false
  interactionRef?: RefObject<HTMLElement | null>
}) {
  const context = useContext(EffectContext)
  const root = useRef<HTMLSpanElement>(null)
  const options = normalizeArtworkEffects(context.options, effects)
  const signature = JSON.stringify(options)
  useEffect(() => {
    if (!root.current) return
    return artworkEffects.register({
      surface: root.current,
      target: interactionRef?.current ?? root.current,
      options,
      reducedMotion: context.reducedMotion,
    })
  }, [signature, context.reducedMotion, interactionRef])
  // The outer frame is deliberately never transformed: pointer bounds cannot feed back
  // into the transform, even when a theme omits an external interactionRef.
  return (
    <span ref={root} className={`winnow-artwork-effects ${className}`}>
      <span className="winnow-artwork-surface">{children}</span>
    </span>
  )
}

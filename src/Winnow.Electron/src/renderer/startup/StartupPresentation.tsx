import { useEffect, useRef, useState } from 'react'
import { SurfacePreparation, createBrowserPreparationClock, type PreparationState } from './preparation'
import { LoadingDragon } from './LoadingDragon'
import './startup.css'

export function useStartupPreparation(
  mode: 'desktop' | 'fullscreen',
  load: () => Promise<void>,
  motion: boolean | undefined,
) {
  const [retry, setRetry] = useState(0)
  const [state, setState] = useState<PreparationState & { mode: string }>({
    mode,
    phase: 'painting',
    opacity: 1,
    tracing: false,
  })
  const latest = useRef({ mode, load, motion })
  latest.current = { mode, load, motion }
  const owner = useRef<SurfacePreparation | null>(null)
  const nativeHidden = useRef(false),
    visibilityVersion = useRef(0)
  owner.current ??= new SurfacePreparation(
    createBrowserPreparationClock(() => nativeHidden.current || document.visibilityState === 'hidden'),
    (value) => setState({ ...value, mode: latest.current.mode }),
  )
  useEffect(
    () =>
      window.winnow.onPresentationVisibility?.((visible) => {
        visibilityVersion.current++
        nativeHidden.current = !visible
        owner.current!.configureMotion(latest.current.motion)
        document.dispatchEvent(new Event('winnow:visibilitychange'))
      }),
    [],
  )
  useEffect(() => {
    const preparation = owner.current!
    let alive = true
    const version = visibilityVersion.current
    void (window.winnow.presentationVisible?.() ?? Promise.resolve(true))
      .then((visible) => {
        if (!alive) return
        if (version === visibilityVersion.current) nativeHidden.current = !visible
        preparation.configureMotion(latest.current.motion)
        void preparation.start(() => latest.current.load())
      })
      .catch(() => {
        if (alive) preparation.fail()
      })
    return () => {
      alive = false
      preparation.cancel()
    }
  }, [mode, retry])
  useEffect(() => {
    owner.current!.configureMotion(motion)
  }, [motion])
  return {
    ...state,
    ...(state.mode !== mode ? { phase: 'painting' as const, opacity: 1, tracing: false } : {}),
    visible: state.mode !== mode || state.phase !== 'ready',
    retry: () => setRetry((value) => value + 1),
    trace: (elapsed: number) => owner.current!.renderedTrace(elapsed),
    fail: () => owner.current!.fail(),
    generation: `${mode}-${retry}`,
  }
}
export function StartupPresentation({
  mode,
  preparation,
  exit,
}: {
  mode: 'desktop' | 'fullscreen'
  preparation: ReturnType<typeof useStartupPreparation>
  exit(): void
}) {
  const panel = useRef<HTMLDivElement>(null)
  const latest = useRef(exit)
  latest.current = exit
  const failed = preparation.phase === 'failed'
  useEffect(() => {
    const element = panel.current!
    const key = (event: KeyboardEvent) => {
      if (event.key === 'F11' || (event.altKey && event.key === 'F4')) return
      if (event.key === 'Escape' && mode === 'fullscreen') {
        event.preventDefault()
        latest.current()
      } else if (event.key === 'Tab') {
        event.preventDefault()
        const buttons = [...element.querySelectorAll<HTMLButtonElement>('button')]
        const index = buttons.indexOf(document.activeElement as HTMLButtonElement)
        buttons[(index + (event.shiftKey ? buttons.length - 1 : 1)) % buttons.length]?.focus()
      } else if (event.key.startsWith('Arrow')) {
        event.preventDefault()
        const buttons = [...element.querySelectorAll<HTMLButtonElement>('button')]
        const index = buttons.indexOf(document.activeElement as HTMLButtonElement)
        buttons[(index + 1) % buttons.length]?.focus()
      } else if (!['Enter', ' '].includes(event.key)) event.preventDefault()
      event.stopPropagation()
    }
    document.addEventListener('keydown', key, true)
    ;(element.querySelector<HTMLButtonElement>('button') ?? element).focus({ preventScroll: true })
    return () => document.removeEventListener('keydown', key, true)
  }, [mode, failed])
  return (
    <div
      ref={panel}
      className={`startup-presentation ${mode}`}
      role="dialog"
      aria-modal="true"
      aria-label={mode === 'fullscreen' ? 'Preparing fullscreen' : 'Preparing your library'}
      tabIndex={-1}
      data-phase={preparation.phase}
      style={{ opacity: preparation.opacity }}
    >
      <div className="startup-stack">
        <LoadingDragon
          key={preparation.generation}
          tracing={preparation.tracing}
          onFrame={preparation.trace}
          onFailure={preparation.fail}
        />
        <strong className="startup-wordmark">WINNOW</strong>
        <p role="status">
          {failed
            ? mode === 'fullscreen'
              ? "Couldn't prepare fullscreen. Try again."
              : "Couldn't prepare your library. Try again."
            : mode === 'fullscreen'
              ? 'Preparing fullscreen…'
              : 'Preparing your library…'}
        </p>
        {failed && <button onClick={preparation.retry}>Try again</button>}
        <button onClick={exit}>{mode === 'fullscreen' ? 'Back to desktop' : 'Enter fullscreen'}</button>
      </div>
    </div>
  )
}

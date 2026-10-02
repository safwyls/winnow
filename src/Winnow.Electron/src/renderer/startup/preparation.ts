export const startupCircuitMilliseconds = 1800
export const startupFadeMilliseconds = 180
export type PreparationState = {
  phase: 'painting' | 'loading' | 'layout' | 'circuit' | 'fading' | 'ready' | 'failed'
  opacity: number
  tracing: boolean
}
export interface PreparationClock {
  frame(signal: AbortSignal): Promise<number>
  hidden(): boolean
}

/** A presentation owns its reveal; shared reads may outlive a departed surface. */
export class SurfacePreparation {
  private cancellation?: AbortController
  private inflight?: Promise<void>
  private motionKnown = false
  private reducedMotion = false
  private circuitComplete = false
  private value: PreparationState = { phase: 'painting', opacity: 1, tracing: false }
  constructor(
    private readonly clock: PreparationClock,
    private readonly changed: (state: PreparationState) => void,
  ) {}
  get state() {
    return this.value
  }
  configureMotion(reduced: boolean | undefined) {
    this.motionKnown = reduced !== undefined
    this.reducedMotion = reduced === true
    this.publish({
      tracing:
        this.active &&
        this.value.phase !== 'painting' &&
        this.motionKnown &&
        !reduced &&
        !this.clock.hidden(),
    })
  }
  renderedTrace(elapsed: number) {
    if (this.active && elapsed >= startupCircuitMilliseconds) this.circuitComplete = true
  }
  private get active() {
    return (
      !!this.cancellation &&
      !this.cancellation.signal.aborted &&
      !['ready', 'failed'].includes(this.value.phase)
    )
  }
  private publish(value: Partial<PreparationState>) {
    this.value = { ...this.value, ...value }
    this.changed(this.value)
  }
  cancel() {
    this.cancellation?.abort()
    this.cancellation = undefined
  }
  fail() {
    this.cancel()
    this.publish({ phase: 'failed', opacity: 1, tracing: false })
  }
  async start(load: () => Promise<void>) {
    this.cancel()
    const cancellation = new AbortController(),
      signal = cancellation.signal
    this.cancellation = cancellation
    this.circuitComplete = false
    this.publish({ phase: 'painting', opacity: 1, tracing: false })
    try {
      await this.clock.frame(signal)
      signal.throwIfAborted()
      this.publish({
        phase: 'loading',
        tracing: this.motionKnown && !this.reducedMotion && !this.clock.hidden(),
      })
      if (!this.inflight) {
        const work = Promise.resolve().then(load)
        this.inflight = work
        void work
          .finally(() => {
            if (this.inflight === work) this.inflight = undefined
          })
          .catch(() => {})
      }
      await waitForRead(this.inflight, signal)
      signal.throwIfAborted()
      this.publish({ phase: 'layout' })
      await this.clock.frame(signal)
      await this.clock.frame(signal)
      signal.throwIfAborted()
      this.publish({ phase: 'circuit' })
      while (!this.clock.hidden() && (!this.motionKnown || (!this.reducedMotion && !this.circuitComplete)))
        await this.clock.frame(signal)
      signal.throwIfAborted()
      if (!this.clock.hidden() && !this.reducedMotion) {
        const start = await this.clock.frame(signal)
        this.publish({ phase: 'fading' })
        let elapsed = 0
        while (elapsed < startupFadeMilliseconds && !this.clock.hidden()) {
          elapsed = (await this.clock.frame(signal)) - start
          signal.throwIfAborted()
          this.publish({ opacity: Math.max(0, 1 - elapsed / startupFadeMilliseconds) })
        }
      }
      signal.throwIfAborted()
      this.publish({ phase: 'ready', opacity: 0, tracing: false })
    } catch {
      if (!signal.aborted) this.publish({ phase: 'failed', opacity: 1, tracing: false })
    }
  }
}

function waitForRead(read: Promise<void>, signal: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    const abort = () => reject(signal.reason)
    signal.addEventListener('abort', abort, { once: true })
    if (signal.aborted) abort()
    void read.then(resolve, reject).finally(() => signal.removeEventListener('abort', abort))
  })
}

export function createBrowserPreparationClock(
  hidden = () => document.visibilityState === 'hidden',
): PreparationClock {
  return {
    hidden,
    frame: (signal) =>
      new Promise((resolve, reject) => {
        let frame = 0
        let paint: PerformanceObserver | undefined
        const clean = () => {
          cancelAnimationFrame(frame)
          paint?.disconnect()
          signal.removeEventListener('abort', abort)
          document.removeEventListener('visibilitychange', visibility)
          document.removeEventListener('winnow:visibilitychange', visibility)
        }
        const finish = (time: number) => {
          clean()
          resolve(time)
        }
        const abort = () => {
          clean()
          reject(signal.reason)
        }
        const visibility = () => {
          if (hidden()) finish(performance.now())
        }
        signal.addEventListener('abort', abort, { once: true })
        document.addEventListener('visibilitychange', visibility)
        document.addEventListener('winnow:visibilitychange', visibility)
        if (signal.aborted) abort()
        else if (hidden()) finish(performance.now())
        // The first callback runs before paint. Cross that paint before releasing a read or layout barrier.
        else
          frame = requestAnimationFrame(() => {
            frame = requestAnimationFrame((time) => {
              if (
                typeof PerformanceObserver !== 'undefined' &&
                PerformanceObserver.supportedEntryTypes.includes('paint') &&
                performance.getEntriesByType('paint').length === 0
              ) {
                paint = new PerformanceObserver(() => finish(performance.now()))
                paint.observe({ type: 'paint', buffered: true })
              } else finish(time)
            })
          })
      }),
  }
}

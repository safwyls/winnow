export interface FetchProgress {
  total: number
  remaining: number
}

export interface FetchStatusSnapshot {
  active: boolean
  remaining: number
  label: string
  remainingText: string
  remainingNote: string
  automationName: string
}

/** A cleared pass ignores late slice reports; the live reporter owns starting the next pass. */
export class FetchStatus {
  private active = false
  private remaining = 0

  begin(total: number) {
    if (total <= 0) return this.clear()
    this.active = true
    this.remaining = total
  }
  report(remaining: number) {
    if (!this.active) return
    if (remaining <= 0) return this.clear()
    this.remaining = remaining
  }
  clear() {
    this.active = false
    this.remaining = 0
  }
  get snapshot(): FetchStatusSnapshot {
    const remainingText = this.remaining.toLocaleString()
    const remainingNote = this.remaining === 1 ? 'title left' : 'titles left'
    return {
      active: this.active,
      remaining: this.remaining,
      label: 'FETCHING DETAILS',
      remainingText,
      remainingNote,
      automationName: `Fetching details, ${remainingText} ${remainingNote}`,
    }
  }
}

export function reportFetchProgress(status: FetchStatus, progress: FetchProgress) {
  if (!Number.isSafeInteger(progress?.total) || !Number.isSafeInteger(progress?.remaining)) return
  if (progress.remaining <= 0) status.clear()
  else {
    if (!status.snapshot.active) status.begin(progress.total)
    status.report(progress.remaining)
  }
}

/** Superseded HTTP reads cannot resurrect a finished pass, even if cancellation arrives late. */
export class FetchProgressObserver {
  private readonly status = new FetchStatus()
  private controller?: AbortController
  private generation = 0
  private queued = false
  private disposed = false
  private paused = false

  constructor(
    private readonly read: (signal: AbortSignal) => Promise<FetchProgress>,
    private readonly publish: (snapshot: FetchStatusSnapshot) => void,
  ) {}

  refresh() {
    if (this.disposed) return
    this.paused = false
    this.generation++
    this.controller?.abort()
    if (this.queued) return
    this.queued = true
    queueMicrotask(() => {
      this.queued = false
      if (this.disposed || this.paused) return
      const generation = this.generation
      const controller = (this.controller = new AbortController())
      void this.read(controller.signal).then(
        (progress) => {
          if (this.disposed || controller.signal.aborted || generation !== this.generation) return
          reportFetchProgress(this.status, progress)
          this.publish(this.status.snapshot)
        },
        () => {
          // Connection state owns transport failures; the caption only describes observed work.
        },
      )
    })
  }

  clear() {
    if (this.disposed) return
    this.paused = true
    this.generation++
    this.controller?.abort()
    this.status.clear()
    this.publish(this.status.snapshot)
  }

  dispose() {
    this.disposed = true
    this.generation++
    this.controller?.abort()
  }
}

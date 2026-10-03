/** Speculation yields to browsing and keeps its own small concurrency allowance. */
export class SettledBackgroundQueue<T> {
  private timer?: ReturnType<typeof setTimeout>
  private active = new Set<AbortController>()
  private queued: T[] = []
  private disposed = false
  private settled = false

  constructor(
    private options: {
      collect(): T[]
      load(item: T, signal: AbortSignal): Promise<void>
      busy(): boolean
    },
  ) {}

  changed = () => {
    if (this.disposed) return
    clearTimeout(this.timer)
    this.settled = false
    this.queued = []
    for (const controller of this.active) controller.abort()
    this.timer = setTimeout(() => {
      this.settled = true
      this.queued = this.options.collect()
      this.drain()
    }, 400)
  }

  dispose() {
    this.disposed = true
    clearTimeout(this.timer)
    this.queued = []
    for (const controller of this.active) controller.abort()
  }

  private drain() {
    if (this.disposed || !this.settled || !this.queued.length) return
    // Covers and requested detail images get their downloads and decoding underway first.
    if (this.options.busy()) {
      clearTimeout(this.timer)
      this.timer = setTimeout(() => this.drain(), 100)
      return
    }
    while (this.active.size < 2 && this.queued.length) {
      const item = this.queued.shift()!
      const controller = new AbortController()
      this.active.add(controller)
      void Promise.resolve()
        .then(() => {
          if (!controller.signal.aborted) return this.options.load(item, controller.signal)
        })
        .catch(() => {
          // Speculative failures are retried through the normal image path on demand.
        })
        .finally(() => {
          this.active.delete(controller)
          this.drain()
        })
    }
  }
}

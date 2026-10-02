import type { BackendEvent } from '../../shared/bridge'

export interface LaunchFeedbackState {
  message: string
  open: boolean
  waiting: boolean
  problem: boolean
}
const closed: LaunchFeedbackState = { message: '', open: false, waiting: false, problem: false }
type PendingLaunch = {
  generation: number
  serial: number
  observed: boolean
  handled: boolean
  readers: number
}

/** A URI handoff is not proof that a game started; only the watcher can confirm it. */
export class LaunchFeedback {
  private state = closed
  private listeners = new Set<() => void>()
  private timer: ReturnType<typeof setTimeout> | undefined
  private ownership: string | null = null
  private title = ''
  private active = true
  private generation = 0
  private serial = 0
  private pending = new Map<string, PendingLaunch>()

  getSnapshot = () => this.state
  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }
  start() {
    this.active = true
  }
  dispose() {
    this.active = false
    this.generation++
    this.pending.clear()
    clearTimeout(this.timer)
  }
  private show(message: string, waiting: boolean, problem: boolean, duration: number) {
    if (!this.active) return
    clearTimeout(this.timer)
    this.state = { message, open: true, waiting, problem }
    this.listeners.forEach((listener) => listener())
    this.timer = setTimeout(() => this.close(), duration)
  }
  waiting(ownershipId: number | string, title: string) {
    if (!this.active) return
    this.ownership = String(ownershipId)
    this.title = title
    this.show(`Starting ${title}…`, true, false, 90_000)
  }
  refused(title: string, store: string) {
    if (!this.active) return
    this.ownership = null
    this.show(`Couldn't reach ${store} to start ${title}.`, false, true, 7_000)
  }
  close() {
    if (!this.active) return
    clearTimeout(this.timer)
    this.ownership = null
    this.state = closed
    this.listeners.forEach((listener) => listener())
  }
  observe = (event: BackendEvent) => {
    if (!this.active || event.kind !== 'launch.observed' || !event.resource) return
    const pending = this.pending.get(event.resource)
    if (pending) pending.observed = true
    if (this.ownership !== event.resource) return
    this.ownership = null
    this.show(`${this.title} is running.`, false, false, 3_000)
  }

  async track(
    ownershipId: number | string,
    title: string,
    store: string,
    action: string,
    send: () => Promise<number>,
  ): Promise<number> {
    if (!this.active) return 1
    if (action !== 'Play') return send()
    const id = String(ownershipId)
    let pending = this.pending.get(id)
    if (!pending) {
      pending = {
        generation: this.generation,
        serial: ++this.serial,
        observed: false,
        handled: false,
        readers: 0,
      }
      this.pending.set(id, pending)
    }
    pending.readers++
    try {
      const result = await send()
      if (
        !this.active ||
        pending.generation !== this.generation ||
        pending.serial !== this.serial ||
        pending.handled
      )
        return result
      if (result === 0) {
        pending.handled = true
        this.waiting(id, title)
        // A warm launcher may produce the observation before the HTTP response arrives.
        if (pending.observed) this.observe({ kind: 'launch.observed', resource: id })
      } else if (result === 2) {
        pending.handled = true
        this.refused(title, store)
      }
      return result
    } finally {
      if (--pending.readers === 0 && this.pending.get(id) === pending) this.pending.delete(id)
    }
  }
}

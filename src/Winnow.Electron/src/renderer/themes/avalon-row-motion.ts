export interface RowMotionSnapshot {
  first: number
  offset: number
  animating: boolean
  realized: number[]
}

/** The original clipped row-window policy, with a clock injectable at the render-frame boundary. */
export class AvalonRowMotion {
  private count = 0
  private visible = 1
  private attached = false
  private reduced = false
  private generation = 0
  private started?: number
  private startOffset = 0
  private listeners = new Set<() => void>()
  private width = 0
  private height = 0
  private state: RowMotionSnapshot = { first: 0, offset: 0, animating: false, realized: [] }
  constructor(private readonly frame: (callback: (timestamp: number) => void) => void) {}
  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }
  snapshot = () => this.state
  configure(count: number, visible: number, first = 0) {
    this.count = Math.max(0, Math.floor(count))
    this.visible = Math.max(1, Math.floor(visible))
    this.stop()
    const target = this.clamp(first)
    this.publish(target, target, false)
  }
  append(count: number) {
    if (count <= this.count) return
    this.count = count
    this.publish()
  }
  attach() {
    this.attached = true
    this.snap()
  }
  detach() {
    this.attached = false
    this.stop()
    this.publish(this.state.first, this.state.first, false, [])
  }
  resize(width: number, height: number) {
    if (width === this.width && height === this.height) return
    this.width = width
    this.height = height
    this.snap()
  }
  reduce(value: boolean) {
    this.reduced = value
    if (value) this.snap()
  }
  show(first: number, animate = true) {
    const target = this.clamp(first)
    if (target === this.state.first) {
      if (this.state.animating && (!animate || this.reduced)) this.snap()
      return
    }
    if (
      !animate ||
      !this.attached ||
      this.reduced ||
      this.height <= 0 ||
      Math.abs(target - this.state.offset) > this.visible + 1
    ) {
      this.stop()
      this.publish(target, target, false)
      return
    }
    this.startOffset = this.state.offset
    this.started = undefined
    const generation = ++this.generation
    this.publish(target, this.state.offset, true)
    const tick = (timestamp: number) => {
      if (!this.attached || generation !== this.generation || !this.state.animating) return
      this.started ??= timestamp
      const progress = Math.max(0, Math.min(1, (timestamp - this.started) / 220))
      if (progress >= 1 || this.reduced) {
        this.snap()
        return
      }
      this.publish(
        this.state.first,
        this.startOffset + (this.state.first - this.startOffset) * (1 - (1 - progress) ** 3),
        true,
      )
      this.frame(tick)
    }
    this.frame(tick)
  }
  private clamp(value: number) {
    return Math.max(0, Math.min(Math.max(0, this.count - this.visible), Math.floor(value)))
  }
  private stop() {
    ++this.generation
    this.started = undefined
  }
  private snap() {
    this.stop()
    this.publish(this.state.first, this.state.first, false)
  }
  private publish(
    first = this.state.first,
    offset = this.state.offset,
    animating = this.state.animating,
    realized?: number[],
  ) {
    if (!realized) {
      let low = Math.max(0, first - 1),
        high = Math.min(this.count - 1, first + this.visible)
      if (animating) {
        low = Math.min(low, Math.max(0, Math.floor(offset)))
        high = Math.max(high, Math.min(this.count - 1, Math.ceil(offset) + this.visible - 1))
      }
      realized = Array.from({ length: Math.max(0, high - low + 1) }, (_, index) => low + index)
    }
    this.state = { first, offset, animating, realized }
    this.listeners.forEach((listener) => listener())
  }
}

export function desktopShelfWidth(width: number): number {
  return Number.isFinite(width) && width > 0
    ? Math.max(180, Math.min(240, Math.floor((width - 72) / 5)))
    : 180
}

/** Size from the unscaled canvas so reducing interface zoom adds capacity rather than taller covers. */
export function homeRowHeight(
  availableWidth: number,
  availableHeight: number,
  canvasHeight: number,
  zoom: number,
): number {
  return homeRowLayout(availableWidth, availableHeight, canvasHeight, zoom).height
}

export function homeRowLayout(
  availableWidth: number,
  availableHeight: number,
  canvasHeight: number,
  zoom: number,
) {
  const height = Math.max(1, availableHeight)
  const referenceHeight = Math.max(1, height + canvasHeight * (1 - 1 / zoom))
  const referenceWidth = Math.max(1, availableWidth * zoom - 76)
  const columns = Math.max(1, Math.ceil(referenceWidth / ((Math.max(1, referenceHeight - 20) * 2) / 3 + 24)))
  const cellWidth = Math.min(referenceWidth / columns, (Math.max(1, height - 20) * 2) / 3 + 24)
  const capacity = Math.max(1, Math.floor((availableWidth - 76 + 0.01) / cellWidth))
  return {
    height: Math.min(height, Math.max(1, ((cellWidth - 24) * 3) / 2 + 20)),
    capacity,
    width: Math.max(1, Math.min(availableWidth - 76, capacity * cellWidth)),
  }
}

/** Change horizontal overflow without asking the browser to scroll the clipped row viewport. */
export function revealShelfCover(cover: HTMLElement): void {
  const row = cover.closest<HTMLElement>('.avalon-home-row, .avalon-desktop-covers')
  if (!row) return
  const bounds = row.getBoundingClientRect(),
    target = cover.getBoundingClientRect()
  const zoom = row.clientWidth > 0 ? bounds.width / row.clientWidth : 1
  if (zoom <= 0) return
  if (target.left < bounds.left) row.scrollLeft += (target.left - bounds.left) / zoom
  else if (target.right > bounds.right) row.scrollLeft += (target.right - bounds.right) / zoom
}

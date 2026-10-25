import { startupCircuitMilliseconds } from './preparation'

export type DragonAppearance = { ink: string; glow: string; size: number }
export type DragonGeometry = { mark: string; contours: { path: string; length: number }[] }
export type DragonProgress = DragonAppearance & { elapsed: number; frames: number; phase: number }
export type DragonFrameScheduler = {
  frame(callback: (time: number) => void): number
  cancel(frame: number): void
}
type DrawingContext = CanvasRenderingContext2D | OffscreenCanvasRenderingContext2D
export function dragonDash(length: number, phase: number) {
  return { pattern: [length * 0.13, length * 0.87], offset: (0.13 - (phase % 1)) * length }
}
export function dragonShapes(geometry: DragonGeometry) {
  return {
    mark: new Path2D(geometry.mark),
    contours: geometry.contours.map((item) => ({ length: item.length, shape: new Path2D(item.path) })),
  }
}
export function drawDragon(
  context: DrawingContext,
  shapes: ReturnType<typeof dragonShapes>,
  appearance: DragonAppearance,
  phase: number,
) {
  const { size, ink, glow } = appearance
  context.clearRect(0, 0, size, size)
  context.save()
  context.scale(size / 560, size / 560)
  context.translate(24, 24)
  context.fillStyle = ink
  context.globalAlpha = 0.65
  context.fill(shapes.mark, 'evenodd')
  context.lineCap = 'round'
  for (const contour of shapes.contours) {
    const dash = dragonDash(contour.length, phase)
    context.setLineDash(dash.pattern)
    context.lineDashOffset = dash.offset
    for (const [width, opacity] of [
      [32, 0.1],
      [18, 0.22],
      [8, 0.65],
      [3, 1],
    ]) {
      context.lineWidth = width
      context.globalAlpha = opacity
      context.strokeStyle = width === 3 ? ink : glow
      context.stroke(contour.shape)
    }
  }
  context.restore()
}

/** Color and size updates share the running circuit; only a new presentation resets it. */
export class DragonRenderer {
  private readonly context: OffscreenCanvasRenderingContext2D
  private readonly shapes: ReturnType<typeof dragonShapes>
  private appearance: DragonAppearance
  private active = false
  private queued: number | undefined
  private started: number | undefined
  private frames = 0
  private lastReport = -Infinity
  constructor(
    private readonly canvas: OffscreenCanvas,
    geometry: DragonGeometry,
    appearance: DragonAppearance,
    private readonly report: (progress: DragonProgress) => void,
    private readonly scheduler: DragonFrameScheduler = {
      frame: (callback) => requestAnimationFrame(callback),
      cancel: (frame) => cancelAnimationFrame(frame),
    },
  ) {
    const context = canvas.getContext('2d')
    if (!context) throw Error('The loading mark could not be drawn.')
    this.context = context
    this.shapes = dragonShapes(geometry)
    this.appearance = appearance
    this.update(appearance)
  }
  update(appearance: DragonAppearance) {
    this.appearance = {
      ink: appearance.ink,
      glow: appearance.glow,
      size: Math.max(1, Math.ceil(appearance.size)),
    }
    if (this.canvas.width !== this.appearance.size) this.canvas.width = this.appearance.size
    if (this.canvas.height !== this.appearance.size) this.canvas.height = this.appearance.size
  }
  start() {
    if (this.active) return
    this.active = true
    this.queue()
  }
  stop() {
    this.active = false
    if (this.queued !== undefined) this.scheduler.cancel(this.queued)
    this.queued = this.started = undefined
    this.frames = 0
    this.lastReport = -Infinity
  }
  private queue() {
    if (this.queued === undefined && this.active) this.queued = this.scheduler.frame(this.draw)
  }
  private draw = (now: number) => {
    this.queued = undefined
    if (!this.active) return
    this.started ??= now
    const elapsed = now - this.started,
      phase = (elapsed % startupCircuitMilliseconds) / startupCircuitMilliseconds
    drawDragon(this.context, this.shapes, this.appearance, phase)
    this.frames++
    if (now - this.lastReport >= 100 || this.frames === 1) {
      this.report({ ...this.appearance, elapsed, frames: this.frames, phase })
      this.lastReport = now
    }
    this.queue()
  }
}

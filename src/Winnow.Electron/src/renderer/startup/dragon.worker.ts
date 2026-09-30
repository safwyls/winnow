type DragonState = {
  canvas: OffscreenCanvas
  contours: { path: string; length: number }[]
  mark: string
  ink: string
  glow: string
  size: number
}
let halt = () => {}
self.onmessage = ({ data }: MessageEvent<DragonState>) => {
  halt()
  const context = data.canvas.getContext('2d')!
  data.canvas.width = data.canvas.height = data.size
  const paths = data.contours.map((item) => ({ ...item, shape: new Path2D(item.path) }))
  const mark = new Path2D(data.mark)
  let started: number | undefined,
    frame = 0,
    frames = 0,
    lastReport = -Infinity
  const draw = (now: number) => {
    started ??= now
    const elapsed = now - started,
      phase = (elapsed % 1800) / 1800
    context.clearRect(0, 0, data.size, data.size)
    context.save()
    context.scale(data.size / 560, data.size / 560)
    context.translate(24, 24)
    context.fillStyle = data.ink
    context.globalAlpha = 0.65
    context.fill(mark, 'evenodd')
    context.lineCap = 'round'
    for (const path of paths) {
      context.setLineDash([path.length * 0.13, path.length * 0.87])
      context.lineDashOffset = (0.13 - phase) * path.length
      for (const [width, opacity] of [
        [32, 0.1],
        [18, 0.22],
        [8, 0.65],
        [3, 1],
      ]) {
        context.lineWidth = width
        context.globalAlpha = opacity
        context.strokeStyle = width === 3 ? data.ink : data.glow
        context.stroke(path.shape)
      }
    }
    context.restore()
    frames++
    if (now - lastReport >= 100 || frames === 1) {
      self.postMessage({ elapsed, frames, phase })
      lastReport = now
    }
    frame = requestAnimationFrame(draw)
  }
  frame = requestAnimationFrame(draw)
  halt = () => cancelAnimationFrame(frame)
}
export {}

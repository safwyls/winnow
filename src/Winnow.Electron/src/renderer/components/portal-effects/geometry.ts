export interface PortalOptions {
  roundness: number
  waviness: number
  activity: number
}

export interface PortalPoint {
  x: number
  y: number
}
export interface PortalRect extends PortalPoint {
  width: number
  height: number
}
export interface PortalGeometry {
  center: PortalPoint
  radius: PortalPoint
  exponent: number
  wave: number
  time: number
}

export const DEFAULT_PORTAL_OPTIONS: Readonly<PortalOptions> = {
  roundness: 70,
  waviness: 45,
  activity: 40,
}
export const PORTAL_OPENING_MS = 360
export const PORTAL_EXPANSION_MS = 680
export const PORTAL_AMBIENT_MS = 1000 / 30

const bounded = (value: number | undefined, fallback: number) =>
  Number.isFinite(value) ? Math.max(0, Math.min(100, value!)) : fallback

export function normalizePortalOptions(options: Partial<PortalOptions> = {}): PortalOptions {
  return {
    roundness: bounded(options.roundness, DEFAULT_PORTAL_OPTIONS.roundness),
    waviness: bounded(options.waviness, DEFAULT_PORTAL_OPTIONS.waviness),
    activity: bounded(options.activity, DEFAULT_PORTAL_OPTIONS.activity),
  }
}

/** Forty matches the original quiet motion; zero does not schedule ambient frames. */
export function portalActivityRate(activity: number): number {
  return (bounded(activity, DEFAULT_PORTAL_OPTIONS.activity) / 40) ** 1.5
}

export function portalGeometry(
  width: number,
  height: number,
  progress: number,
  options: PortalOptions,
  time = 0,
  origin?: PortalPoint,
  expansion?: PortalRect,
): PortalGeometry {
  const rest = { x: width / 2, y: height / 2 }
  const start = expansion
    ? { x: expansion.x + expansion.width / 2, y: expansion.y + expansion.height / 2 }
    : (origin ?? rest)
  const reveal = 1 - (1 - Math.max(0, Math.min(1, progress))) ** 3
  const scale = 0.025 + 0.975 * reveal
  return {
    center: { x: start.x + (rest.x - start.x) * reveal, y: start.y + (rest.y - start.y) * reveal },
    radius: {
      // Overscan clears every corner, including the roundest and most active contour.
      x: Math.max(
        1,
        expansion ? (expansion.width / 2 - 8) * (1 - reveal) + rest.x * 1.5 * reveal : (rest.x - 8) * scale,
      ),
      y: Math.max(
        1,
        expansion ? (expansion.height / 2 - 8) * (1 - reveal) + rest.y * 1.5 * reveal : (rest.y - 8) * scale,
      ),
    },
    exponent: 5.8 - options.roundness * 0.028,
    wave: options.waviness * 0.08 * scale,
    time,
  }
}

export function portalContour(geometry: PortalGeometry): PortalPoint[] {
  const { center, radius, exponent, wave, time } = geometry
  return Array.from({ length: 128 }, (_, i) => {
    const angle = (i * Math.PI) / 64,
      x = Math.cos(angle),
      y = Math.sin(angle)
    const ripple = (Math.sin(angle * 3 + time * 0.32) + Math.sin(angle * 7 - time * 0.23) * 0.62) * wave
    const r =
      (1 - ripple / Math.max(1, Math.min(radius.x, radius.y))) /
      (Math.abs(x) ** exponent + Math.abs(y) ** exponent) ** (1 / exponent)
    return { x: center.x + x * radius.x * r, y: center.y + y * radius.y * r }
  })
}

export function portalPolygon(geometry: PortalGeometry): string {
  return `polygon(${portalContour(geometry)
    .map(({ x, y }) => `${x.toFixed(2)}px ${y.toFixed(2)}px`)
    .join(',')})`
}

/** A conservative bound: the entire pane is inside the edge and its visible halo. */
export function portalCoversSurface(geometry: PortalGeometry, width: number, height: number): boolean {
  const { center, radius, exponent, wave } = geometry
  const x = Math.max(Math.abs(center.x), Math.abs(width - center.x)) / radius.x
  const y = Math.max(Math.abs(center.y), Math.abs(height - center.y)) / radius.y
  const contour = (x ** exponent + y ** exponent) ** (1 / exponent)
  // The largest ripple is 1.62 * wave; 64px includes the shader's fading halo.
  return (contour - 1) * Math.min(radius.x, radius.y) + 1.62 * Math.abs(wave) < -64
}

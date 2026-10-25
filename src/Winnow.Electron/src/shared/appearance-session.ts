export interface AppearanceSession {
  palette: string | null
  transparency: number
  backdrop: 'acrylic' | 'mica'
  wallTranslucent: boolean
  layout: 'floating' | 'flush'
}

/** The original capture switches exist only in development builds. */
export function appearanceSession(args: readonly string[], packaged: boolean): AppearanceSession | null {
  if (packaged) return null
  const option = (name: string) =>
    args.find((value) => value.startsWith(`--${name}=`))?.slice(name.length + 3)
  const palette = option('theme'),
    amount = option('transparency'),
    backdrop = option('backdrop'),
    wall = option('wall'),
    layout = option('layout'),
    transparent = args.includes('--transparent')
  if ([palette, amount, backdrop, wall, layout].every((value) => value === undefined) && !transparent)
    return null
  let percent = transparent ? 100 : 0
  if (amount != null && /^\s*[+-]?\d+\s*$/.test(amount)) {
    const parsed = Number(amount)
    if (parsed >= -2147483648 && parsed <= 2147483647) percent = parsed
  }
  return {
    palette: palette ?? null,
    transparency: Math.max(0, Math.min(100, percent)),
    backdrop: backdrop === 'mica' ? 'mica' : 'acrylic',
    wallTranslucent: wall === undefined || ['on', 'true', '1'].includes(wall),
    layout: layout === 'flush' ? 'flush' : 'floating',
  }
}

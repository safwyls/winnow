/** Shared material settings; themes can use these without adopting a host card layout. */
export interface ArtworkEffectOptions {
  finish: 'off' | 'matte' | 'satin' | 'foil'
  intensity: number
  followPointer: boolean
  floating: boolean
  tilt: number
  highlightFoil: boolean
  foilMetal: 'silver' | 'gold' | 'holographic'
  foilStrength: number
  foilThreshold: number
}

export const DEFAULT_ARTWORK_EFFECTS: Readonly<ArtworkEffectOptions> = Object.freeze({
  finish: 'satin',
  intensity: 55,
  followPointer: true,
  floating: true,
  tilt: 7,
  highlightFoil: true,
  foilMetal: 'silver',
  foilStrength: 65,
  foilThreshold: 72,
})

/** Bound developer overrides separately from strict imported-profile validation. */
export function normalizeArtworkEffects(
  base?: Partial<ArtworkEffectOptions>,
  overrides?: Partial<ArtworkEffectOptions> | false,
): ArtworkEffectOptions {
  const result = { ...DEFAULT_ARTWORK_EFFECTS }
  for (const values of [base, overrides || undefined]) {
    if (!values) continue
    if (['off', 'matte', 'satin', 'foil'].includes(values.finish ?? '')) result.finish = values.finish!
    if (['silver', 'gold', 'holographic'].includes(values.foilMetal ?? ''))
      result.foilMetal = values.foilMetal!
    for (const key of ['followPointer', 'floating', 'highlightFoil'] as const)
      if (typeof values[key] === 'boolean') result[key] = values[key]
    for (const [key, min, max] of [
      ['intensity', 0, 100],
      ['tilt', 0, 12],
      ['foilStrength', 0, 100],
      ['foilThreshold', 40, 95],
    ] as const) {
      const value = values[key]
      if (typeof value === 'number' && Number.isFinite(value))
        result[key] = Math.min(max, Math.max(min, value))
    }
  }
  if (overrides === false) {
    result.finish = 'off'
    result.highlightFoil = false
    result.floating = false
  }
  return result
}

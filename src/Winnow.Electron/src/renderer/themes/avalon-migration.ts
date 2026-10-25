import { DEFAULT_PROFILE, type ThemeProfile } from '../../shared/theme'
import { avalonPalette } from './avalon-palettes'
import { parseTypography } from '../../shared/typography'

/** PresentationPreference.Theme reads the original appearance.theme setting. */
export function migrateAvaloniaPalette(preferences: unknown): {
  profile: ThemeProfile
  unavailable: boolean
} {
  const profile = structuredClone(DEFAULT_PROFILE)
  const typography: unknown = Array.isArray(preferences)
    ? preferences.find((row) => row?.preference === 'Typography')?.value
    : null
  try {
    const entries: unknown = typeof typography === 'string' ? JSON.parse(typography) : typography
    if (entries && typeof entries === 'object' && !Array.isArray(entries)) {
      for (const [key, value] of Object.entries(entries)) {
        const id = key === 'hoard' && !avalonPalette(key) ? 'winnow' : key
        if (!avalonPalette(id)) continue
        try {
          ;(profile.appearance.typography ??= {})[id] = parseTypography(value)
        } catch {
          /* One damaged palette must not discard other saved fonts. */
        }
      }
    }
  } catch {
    /* Malformed preferences keep the bundled role defaults. */
  }
  const stored: unknown = Array.isArray(preferences)
    ? preferences.find((row) => row?.preference === 'Theme')?.value
    : null
  if (stored == null || stored === '') return { profile, unavailable: false }
  const id = stored === 'hoard' && !avalonPalette(stored) ? 'winnow' : stored
  if (typeof id !== 'string' || !avalonPalette(id))
    return { profile, unavailable: true }
  profile.settings.avalon = { palette: id }
  return { profile, unavailable: false }
}

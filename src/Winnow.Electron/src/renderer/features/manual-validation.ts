export type ManualField = 'title' | 'year' | 'igdbId' | 'steamAppId'
export type ManualFieldErrors = Partial<Record<ManualField, string>>

export function validateManualDraft(draft: Record<ManualField, string>): ManualFieldErrors {
  if (!draft.title.trim()) return { title: 'A title is needed.' }
  if (
    draft.year.trim() &&
    (!/^\d+$/.test(draft.year.trim()) || Number(draft.year) < 1900 || Number(draft.year) > 2200)
  )
    return { year: 'A year from 1900 to 2200.' }
  if (draft.igdbId.trim() && (!/^\d+$/.test(draft.igdbId.trim()) || Number(draft.igdbId) <= 0))
    return { igdbId: 'A positive numeric ID.' }
  // JSON numbers cannot carry arbitrary 64-bit identifiers without rounding them.
  if (draft.igdbId.trim() && !Number.isSafeInteger(Number(draft.igdbId)))
    return { igdbId: 'This ID is too large to save exactly.' }
  if (
    draft.steamAppId.trim() &&
    (!/^\d+$/.test(draft.steamAppId.trim()) ||
      Number(draft.steamAppId) <= 0 ||
      Number(draft.steamAppId) > 4294967295)
  )
    return { steamAppId: 'A positive numeric ID, up to 4294967295.' }
  return {}
}

export function manualFieldConflict(problem: unknown): ManualFieldErrors | null {
  if (!problem || typeof problem !== 'object') return null
  const { field, reason } = problem as { field?: unknown; reason?: unknown }
  const target = field === 'SteamAppId' ? 'steamAppId' : field === 'IgdbId' ? 'igdbId' : null
  if (!target) return null
  const message =
    reason === 'LegacyIdentifierHistory'
      ? 'The origin of the old ID is unknown. Keep it to edit details, or add a separate game with the corrected ID.'
      : reason === 'StorefrontObservation'
        ? 'A store entry uses this ID. Keep it to edit details, or add the corrected game separately.'
        : reason === 'MappingChanged'
          ? "The game's IGDB match changed. Cancel and reopen this form before saving."
          : 'Another game in your library already has this.'
  return { [target]: message }
}

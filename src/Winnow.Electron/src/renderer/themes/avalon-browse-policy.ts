const alphabet = ['#', ...'ABCDEFGHIJKLMNOPQRSTUVWXYZ']
export const SPINE_STOPS = alphabet.length

export function alphabetSection(title: string | null | undefined): string {
  for (const rune of (title ?? '').trimStart().normalize('NFD')) {
    if (/\p{Mn}/u.test(rune)) continue
    const upper = rune.toUpperCase()
    return /^[A-Z]$/.test(upper) ? upper : '#'
  }
  return '#'
}

export function browseSections(games: readonly { title: string }[], sort: string) {
  const first = new Map<string, number>()
  games.forEach((game, index) => {
    const section = alphabetSection(game.title)
    if (!first.has(section)) first.set(section, index)
  })
  return (sort === 'title-desc' ? [...alphabet].reverse() : alphabet).map((label) => ({
    label,
    index: first.get(label),
  }))
}

export const isAlphabetSort = (sort: string) => sort === 'title' || sort === 'title-desc'
const clamp = (value: number, max: number) => Math.max(0, Math.min(max, value))
export const spinePointerRow = (y: number, height: number) =>
  height > 0 ? clamp((y / height) * SPINE_STOPS - 0.5, SPINE_STOPS - 1) : 0

export function nearestSection(sections: ReturnType<typeof browseSections>, row: number) {
  let best: { index: number; distance: number } | undefined
  sections.forEach((section, stop) => {
    const distance = Math.abs(stop - row)
    if (section.index !== undefined && (!best || distance < best.distance))
      best = { index: section.index, distance }
  })
  return best?.index
}

export function spineLocation(games: readonly { title: string }[], sort: string, proportion: number) {
  const progress = clamp(proportion, 1)
  if (!isAlphabetSort(sort) || !games.length) return progress * (SPINE_STOPS - 1)
  const position = progress * (games.length - 1)
  const lower = Math.floor(position),
    upper = Math.ceil(position)
  const row = (index: number) => {
    const ascending = alphabet.indexOf(alphabetSection(games[index].title))
    return sort === 'title-desc' ? SPINE_STOPS - 1 - ascending : ascending
  }
  return row(lower) + (row(upper) - row(lower)) * (position - lower)
}

export function spineWave(stop: number, pointer: number | null) {
  const distance = pointer === null ? 4 : Math.abs(stop - pointer)
  return distance < 4 ? (-13 * (1 + Math.cos((Math.PI * distance) / 4))) / 2 : 0
}

export function spineHalo(stop: number, location: number) {
  const distance = Math.abs(stop - location)
  return distance <= 0.5 ? 4 : distance <= 1.5 ? 3 : distance <= 2.5 ? 2 : distance <= 3.5 ? 1 : 0
}

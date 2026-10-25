import type { LibraryGame } from '../api/types'

const labels: Record<string, string> = {
  steam: 'Steam',
  epic: 'Epic',
  gog: 'GOG',
  'plugin:xbox': 'Xbox',
  'plugin:psn': 'PlayStation',
}

/** One mark per store, preserving the primary copy's order even across multiple accounts. */
export function ownershipStores(game: LibraryGame) {
  const stores = new Map<string, { key: string; label: string; badge: string; initial: string }>()
  for (const entry of game.entries) {
    const key = entry.store.toLowerCase()
    if (stores.has(key)) continue
    const name = key.startsWith('plugin:') ? entry.store.slice(7) : entry.store
    const label = labels[key] ?? name.slice(0, 1).toUpperCase() + name.slice(1)
    stores.set(key, { key, label, badge: label.toUpperCase(), initial: label.slice(0, 1).toUpperCase() })
  }
  return [...stores.values()]
}

export function ownershipDescription(game: LibraryGame): string {
  const stores = ownershipStores(game)
  return stores.length > 1 ? `. Owned on ${stores.map((store) => store.label).join(', ')}` : ''
}

import { storeLabel } from './client'
import type { LibraryGame, UpdateEvent, Workspace } from './types'

export interface GameLink {
  label: string
  url: string
  detail?: string
}

function webLink(value?: string | null): string | undefined {
  if (!value || value.length > 4096 || /[\u0000-\u0020]/.test(value)) return
  try {
    const url = new URL(value)
    if (
      url.protocol === 'https:' &&
      !url.username &&
      !url.password &&
      !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
    )
      return url.href
  } catch {
    // Missing or malformed provider URLs do not offer a destination.
  }
}

export function gameLinks(game: LibraryGame, workspace: Workspace, events: UpdateEvent[] = []): GameLink[] {
  const links: GameLink[] = []
  const releases = new Set(game.entries.map((entry) => entry.releaseId))
  const ids = workspace.externalIds.filter((id) => releases.has(id.releaseId))
  const steamIds = [
    ...new Set(
      ids
        .filter(
          (id) => id.provider === 'steam' && /^\d{1,10}$/.test(id.providerId) && Number(id.providerId) > 0,
        )
        .map((id) => id.providerId),
    ),
  ]
  for (const appId of steamIds) {
    const releaseIds = new Set(
      ids.filter((id) => id.provider === 'steam' && id.providerId === appId).map((id) => id.releaseId),
    )
    const entries = game.entries.filter((entry) => releaseIds.has(entry.releaseId))
    const detail = steamIds.length > 1 ? `${entries[0]?.title ?? game.title} · ${appId}` : undefined
    links.push(
      {
        label: 'View in Steam',
        url: entries.some((entry) => entry.store === 'steam')
          ? `steam://nav/games/details/${appId}`
          : `steam://store/${appId}`,
        detail,
      },
      { label: 'Store page', url: `https://store.steampowered.com/app/${appId}/`, detail },
      { label: 'Patch notes', url: `https://store.steampowered.com/news/app/${appId}`, detail },
      { label: 'SteamDB', url: `https://steamdb.info/app/${appId}/`, detail },
      { label: 'SteamGridDB', url: `https://www.steamgriddb.com/steam/${appId}`, detail },
    )
  }
  for (const entry of game.entries) {
    if (entry.store !== 'epic' && entry.store !== 'gog') continue
    const id = ids.find((id) => id.releaseId === entry.releaseId && id.provider === entry.store)?.providerId
    const key = entry.store === 'epic' ? workspace.epicLaunchKeys[id ?? '']?.namespace : id
    const url = key && webLink(workspace.storefronts?.[`${entry.store}:${key}`]?.storeUrl)
    if (url) links.push({ label: `${storeLabel(entry.store)} store page`, url })
    if (entry.store === 'gog' && id && /^\d{1,12}$/.test(id))
      links.push({ label: 'Show in GOG Galaxy', url: `goggalaxy://opengameview/gog_${id}` })
  }
  if (!steamIds.length) {
    const latest = events
      .filter((event) => releases.has(event.releaseId) && webLink(event.url))
      .sort((a, b) => b.occurredAt.localeCompare(a.occurredAt))[0]
    const url = webLink(latest?.url)
    if (url) links.push({ label: 'Latest patch notes', url })
  }
  const igdbId = workspace.works.find((work) => work.id === game.workId)?.igdbId
  // IGDB short links use base 36; /games/ takes a slug, not a decimal game ID.
  if (typeof igdbId === 'number' && Number.isSafeInteger(igdbId) && igdbId > 0)
    links.push({ label: 'IGDB', url: `https://www.igdb.com/g/${igdbId.toString(36)}` })
  return links.filter((link, index) => links.findIndex((other) => other.url === link.url) === index)
}

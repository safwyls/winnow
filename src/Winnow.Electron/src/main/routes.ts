import type { ApiRequest } from '../shared/bridge'
import { ownershipId } from '../shared/ownership-id'

type Route = readonly [method: 'GET' | 'POST' | 'PUT' | 'DELETE', path: string, query?: readonly string[]]
/** This registry is the renderer's authority: no arbitrary URLs, headers or HTTP methods. */
export const routes: Record<string, Route> = {
  'health.get': ['GET', 'health'],
  'capabilities.get': ['GET', 'capabilities'],
  'library.get': ['GET', 'library'],
  'library.workspace': ['GET', 'library/workspace'],
  'library.visibility': ['GET', 'library/visibility-counts'],
  'library.derelict-exemptions': ['POST', 'library/derelict-exemptions'],
  'game.get': ['GET', 'games/:workId'],
  'game.details': ['GET', 'games/:workId/details'],
  'game.refetch': ['POST', 'games/:workId/refetch'],
  'feed.get': ['GET', 'feed'],
  'feed.supplement': ['GET', 'feed/supplement'],
  'feed.history': ['GET', 'feed/history'],
  'feed.impressions': ['POST', 'feed/impressions'],
  'feed.feedback': ['POST', 'feed/feedback'],
  'feed.revoke': ['POST', 'feed/feedback/revoke'],
  'activity.query': ['POST', 'activity/query'],
  'activity.steam': ['POST', 'activity/steam'],
  'statistics.gameplay': ['POST', 'statistics/gameplay'],
  'statistics.account': ['GET', 'statistics/accounts/:source'],
  'journal.get': ['GET', 'sessions/:sessionId/journal'],
  'journal.put': ['PUT', 'sessions/:sessionId/journal'],
  'journal.delete': ['DELETE', 'sessions/:sessionId/journal'],
  'journal.prompt': ['GET', 'sessions/:sessionId/prompt'],
  'journal.preferences.get': ['GET', 'journal/preferences'],
  'journal.preferences.put': ['PUT', 'journal/preferences'],
  'list.create': ['POST', 'lists'],
  'list.live': ['POST', 'lists/live'],
  'list.update': ['PUT', 'lists/:listId'],
  'list.delete': ['DELETE', 'lists/:listId'],
  'list.member.add': ['POST', 'lists/:listId/members'],
  'list.member.remove': ['DELETE', 'lists/:listId/members'],
  'list.order': ['PUT', 'lists/:listId/order'],
  'list.filter': ['PUT', 'lists/:listId/filter'],
  'manual.get': ['GET', 'manual-games'],
  'manual.detail': ['GET', 'manual-games/:ownershipId'],
  'manual.create': ['POST', 'manual-games'],
  'manual.update': ['PUT', 'manual-games/:ownershipId'],
  'manual.delete': ['DELETE', 'manual-games/:ownershipId'],
  'hidden.get': ['GET', 'hidden-games'],
  'hidden.put': ['PUT', 'hidden-games'],
  'identity.get': ['GET', 'identity/review/'],
  'identity.link': ['POST', 'identity/review/link'],
  'identity.dismiss': ['POST', 'identity/review/dismiss'],
  'identity.undo': ['POST', 'identity/review/undo'],
  'identity.header': ['PUT', 'identity/review/header'],
  'identity.refresh': ['POST', 'identity/review/refresh'],
  'identity.separate': ['DELETE', 'identity/links/:childWorkId'],
  'metadata.get': ['GET', 'games/:workId/metadata'],
  'metadata.put': ['PUT', 'games/:workId/metadata'],
  'metadata.reset': ['POST', 'games/:workId/metadata/reset'],
  'metadata.search': ['GET', 'metadata/igdb/search', ['title']],
  'metadata.candidate': ['GET', 'metadata/igdb/:igdbId'],
  'metadata.claiming': ['GET', 'metadata/igdb/:igdbId/claiming-game'],
  'metadata.art-upload': ['POST', 'games/:workId/metadata/art-upload'],
  'metadata.art-download': ['POST', 'games/:workId/metadata/art-download'],
  'metadata.igdb': ['GET', 'games/:workId/igdb/state'],
  'metadata.assign': ['PUT', 'games/:workId/igdb'],
  'metadata.clear': ['DELETE', 'games/:workId/igdb'],
  'updates.acknowledge': ['POST', 'releases/:releaseId/acknowledge-updates'],
  'updates.restore': ['POST', 'releases/:releaseId/restore-updates'],
  'updates.acknowledgement': ['GET', 'releases/:releaseId/acknowledgement'],
  'artwork.sources': ['GET', 'artwork/sources'],
  'artwork.backdrop': ['GET', 'works/:workId/backdrop', ['aspectRatio']],
  'artwork.get': ['GET', 'works/:workId/artwork/:slot'],
  'artwork.browse': ['GET', 'works/:workId/artwork/:slot/browse', ['source', 'cursor']],
  'artwork.put': ['PUT', 'works/:workId/artwork/:slot'],
  'artwork.reset': ['POST', 'works/:workId/artwork/:slot/reset'],
  'artwork.url': ['POST', 'works/:workId/artwork/:slot/url'],
  'connections.get': ['GET', 'connections/stores'],
  'connections.igdb.get': ['GET', 'connections/igdb'],
  'connections.igdb.put': ['PUT', 'connections/igdb'],
  'connections.igdb.delete': ['DELETE', 'connections/igdb'],
  'connections.steam.key': ['PUT', 'connections/stores/steam/key'],
  'connections.steam.signin': ['POST', 'connections/stores/steam/sign-in'],
  'connections.steam.complete': ['POST', 'connections/stores/steam/sign-in/complete'],
  'connections.steam.signout': ['POST', 'connections/stores/steam/sign-out'],
  'connections.epic.signin': ['POST', 'connections/stores/epic/sign-in'],
  'connections.epic.complete': ['POST', 'connections/stores/epic/sign-in/complete'],
  'connections.epic.signout': ['POST', 'connections/stores/epic/sign-out'],
  'connections.cancel': ['POST', 'connections/stores/sign-in/cancel'],
  'connections.visibility.get': ['GET', 'connections/account-visibility'],
  'connections.visibility.put': ['PUT', 'connections/account-visibility'],
  'plugins.get': ['GET', 'connections/plugins'],
  'plugins.settings': ['PUT', 'connections/plugins/:pluginId/settings'],
  'plugins.removeSecret': ['DELETE', 'connections/plugins/:pluginId/secrets/:key'],
  'plugins.directory': ['GET', 'connections/plugins/directory'],
  'plugins.enabled': ['PUT', 'connections/plugins/:pluginId/enabled'],
  'plugins.refresh': ['POST', 'connections/plugins/:pluginId/refresh'],
  'plugins.signin': ['POST', 'connections/plugins/:pluginId/sign-in'],
  'plugins.poll': ['POST', 'connections/plugins/:pluginId/sign-in/poll'],
  'plugins.cancel': ['POST', 'connections/plugins/:pluginId/sign-in/cancel'],
  'plugins.signout': ['POST', 'connections/plugins/:pluginId/sign-out'],
  'preferences.library.get': ['GET', 'preferences/library'],
  'preferences.library.put': ['PUT', 'preferences/library'],
  'preferences.presentation.get': ['GET', 'preferences/presentation'],
  'preferences.artworkSources': ['GET', 'preferences/artwork-sources'],
  'preferences.presentation.put': ['PUT', 'preferences/presentation/:preference'],
  'setup.get': ['GET', 'setup'],
  'setup.put': ['PUT', 'setup'],
  'operations.get': ['GET', 'operations'],
  'operations.detail': ['GET', 'operations/:id'],
  'operations.metadata': ['POST', 'operations/metadata-sync'],
  'operations.plugin': ['POST', 'operations/plugin-install'],
  'operations.cancel': ['POST', 'operations/:id/cancel'],
  'progress.get': ['GET', 'progress'],
  'diagnostics.get': ['GET', 'diagnostics'],
  'acquisitions.export': ['GET', 'exports/acquisitions'],
  'imports.steam.load': ['POST', 'imports/steam/load-files'],
  'imports.steam.pages': ['POST', 'imports/steam/pages'],
  'actions.execute': ['POST', 'entries/:ownershipId/actions'],
}
const aliases: Record<string, string> = {
  artworkState: 'artwork.get',
  feedImpression: 'feed.impressions',
  feedFeedback: 'feed.feedback',
  feedRevoke: 'feed.revoke',
  feedHistory: 'feed.history',
  'connections.steam.signOut': 'connections.steam.signout',
  'connections.epic.signOut': 'connections.epic.signout',
  'connections.steam.signIn': 'connections.steam.signin',
  'connections.epic.signIn': 'connections.epic.signin',
  'plugins.signIn': 'plugins.signin',
  'plugins.signOut': 'plugins.signout',
  'operations.sync': 'operations.metadata',
}
export function resolveRoute(request: ApiRequest): { method: string; path: string; body?: string } {
  if (!request || typeof request !== 'object' || typeof request.route !== 'string')
    throw new Error('Invalid request')
  const name = Object.hasOwn(aliases, request.route) ? aliases[request.route] : request.route
  if (!Object.hasOwn(routes, name)) throw new Error('Unsupported request')
  const [method, template, queryKeys = []] = routes[name]
  const params = request.params ?? {}
  if (typeof params !== 'object' || Array.isArray(params)) throw new Error('Invalid parameters')
  const used = new Set(queryKeys)
  const path = template.replace(/:([a-zA-Z]+)/g, (_, key: string) => {
    used.add(key)
    let value = params[key]
    if (key === 'slot' && value === 'Background') value = 'Hero'
    if (typeof value !== 'number' && typeof value !== 'string') throw new Error(`Missing ${key}`)
    const text = String(value)
    if (key === 'ownershipId') {
      if (ownershipId(value) === null) throw new Error(`Invalid ${key}`)
    } else if (['workId', 'releaseId', 'listId', 'sessionId', 'childWorkId', 'igdbId'].includes(key)) {
      if (!/^[1-9]\d{0,15}$/.test(text) || !Number.isSafeInteger(Number(text)))
        throw new Error(`Invalid ${key}`)
    } else if (key === 'slot') {
      if (!['Hero', 'Cover', 'Icon'].includes(text)) throw new Error('Invalid artwork slot')
    } else if (!/^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$/.test(text) || text.includes('..'))
      throw new Error(`Invalid ${key}`)
    return encodeURIComponent(text)
  })
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (!used.has(key)) throw new Error(`Unsupported parameter ${key}`)
    if (queryKeys.includes(key)) {
      if (!['string', 'number'].includes(typeof value) || String(value).length > 2048)
        throw new Error(`Invalid ${key}`)
      query.set(key, String(value))
    }
  }
  if (method === 'GET' && request.body !== undefined) throw new Error('Read requests cannot include commands')
  const body = request.body === undefined ? undefined : JSON.stringify(request.body)
  const limit =
    name === 'metadata.art-upload'
      ? 28 * 1024 * 1024
      : name === 'imports.steam.load'
        ? 180 * 1024 * 1024
        : name === 'imports.steam.pages'
          ? 140 * 1024 * 1024
          : 2 * 1024 * 1024
  if (body && Buffer.byteLength(body) > limit) throw new Error('Request is too large')
  return { method, path: `/api/v1/${path}${query.size ? `?${query}` : ''}`, body }
}

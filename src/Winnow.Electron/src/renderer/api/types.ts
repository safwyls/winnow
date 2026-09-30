export type Mode = 'desktop' | 'fullscreen'
export interface GameEntry {
  ownershipId: number
  releaseId: number
  workId: number
  title: string
  store: string
  platform?: string | null
  installed: boolean
  playtimeMinutes: number
  lastPlayedAt?: string | null
}
export interface LibraryGame {
  workId: number
  title: string
  firstReleaseYear?: number | null
  summary?: string | null
  publisher?: string | null
  coverUrl?: string | null
  backgroundUrl?: string | null
  bucket: string
  playtimeMinutes: number
  lastPlayedAt?: string | null
  entries: GameEntry[]
}
export interface GameList {
  id: number
  name: string
  description?: string | null
  isLive: boolean
  releaseIds: number[]
  revision: string
  filter?: LibraryFilter | null
}
export interface LibraryFilter {
  stores?: string[]
  buckets?: string[]
  installed?: boolean | null
  search?: string | null
  [key: string]: unknown
}
export interface LibraryResponse {
  games: LibraryGame[]
  lists: GameList[]
}
export interface FeedItem {
  ownershipId: number
  releaseId: number
  title: string
  reason: string
}
export interface FeedShelf {
  id: string
  title: string
  blurb: string
  items: FeedItem[]
  reserve: FeedItem[]
  supportsFeedback: boolean
}
export interface FeedSnapshot {
  shelves: FeedShelf[]
  candidateCount: number
  confidence: number
  failed: boolean
}
export interface FeedVerdict {
  releaseId: number
  kind: number
  createdAt: string
  expiresAt?: string | null
  revokedAt?: string | null
  status: number
}
export interface LibraryPreferences {
  showNonGameEntries: boolean
  showExplicitContent: boolean
  maturityCap: string
}
export interface Workspace {
  preferences: LibraryPreferences
  externalIds: { releaseId: number; provider: string; providerId: string }[]
  epicLaunchKeys: Record<string, { namespace: string; catalogItemId: string; artifactId: string }>
  pluginActions: Record<string, { sourceLabel?: string | null; canPlay: boolean; canOpenStore: boolean }>
  works: { id: number; name: string; igdbId?: number | null; nameIsProvisional?: boolean }[]
  storefronts?: Record<string, { storeUrl?: string | null; patchNotes?: string | null }>
  [key: string]: unknown
}
export interface Session {
  id: number
  ownershipId: number
  startedAt: string
  endedAt?: string | null
  durationSeconds?: number | null
  detectionMethod: string
}
export interface JournalResponse {
  sessionId: number
  note?: string | null
  rating?: number | null
  revision: string
}
export interface UpdateEvent {
  id: number
  releaseId: number
  kind: string
  occurredAt: string
  title?: string | null
  buildId?: string | null
  url?: string | null
}
export interface GameDetails {
  workId: number
  readAtUtc: string
  events: UpdateEvent[]
  sessions: Record<string, Session[]>
  journalEntries: {
    sessionId: number
    ownershipId: number
    sessionAt: string
    note?: string | null
    rating?: number | null
  }[]
  ratings: {
    source: string
    score?: number | null
    ratingCount?: number | null
    label?: string | null
    observedAt: string
    hasFigure: boolean
  }[]
  achievements: {
    releaseId: number
    total: number
    unlocked: number
    hasKnownProgress: boolean
    isStale: boolean
    percentComplete?: number | null
  }[]
  [key: string]: unknown
}
export interface ActivityRow {
  ownershipId: number
  store: string
  atUtc: string
  session?: Session | null
  note?: { sessionId: number; note?: string | null; rating?: number | null } | null
  update?: UpdateEvent | null
}
export interface ActivityCursor {
  atUtc: string
  id: number
}
export interface ActivityPage {
  rows: ActivityRow[]
  next?: ActivityCursor | null
}
export interface GameplayStats {
  recordedSeconds: number
  gamesPlayedCount: number
  startedSessionCount: number
  overlappingSessionCount: number
  excludedSessionCount: number
  medianSessionSeconds?: number | null
  periods: { fromUtc: string; untilUtc: string; recordedSeconds: number }[]
  topGames: { resolvedWorkId: number; recordedSeconds: number }[]
}
export interface Metadata {
  workId: number
  title: string
  isPinned: boolean
  fields: { field: string; value?: string | null; source?: string | null }[]
  revision: string
}
export interface CoverKey {
  provider: string
  id: string
}
export interface ArtworkCandidate {
  sourceId: string
  sourceName: string
  assetId: string
  previewKey: CoverKey
  offerId?: string | null
  creator?: string | null
  thumbnailKey?: CoverKey | null
  pageUrl?: string | null
  width?: number | null
  height?: number | null
  isCurrent: boolean
}
export interface ArtworkState {
  current?: ArtworkCandidate | null
  revision: string
}
export interface ArtworkPage {
  items: ArtworkCandidate[]
  nextCursor?: string | null
  message?: string | null
  canRetry: boolean
}
export interface StoreConnections {
  steam: {
    hasApiKey: boolean
    apiKeyIsAppManaged: boolean
    hasSession: boolean
    sessionUsable: boolean
    hasUsableCredential: boolean
    sessionAccount?: string | null
    sessionExpiresAt?: string | null
  }
  steamHealth?: number
  epic?: { isLive: boolean; displayName?: string | null } | null
}
export interface IgdbConnection {
  clientId: string
  hasSavedCredentials: boolean
  isReadable: boolean
  hasConfigurationCredentials: boolean
}
export interface PluginSetting {
  key: string
  label: string
  description?: string | null
  isSecret: boolean
  isRequired: boolean
  value?: string | null
  hasStoredSecret: boolean
  setupUrl?: string | null
  isBoolean: boolean
  isAdvanced: boolean
}
export interface PluginSnapshot {
  id: string
  name: string
  description: string
  version: string
  capabilities: string
  enabled: boolean
  isLoaded: boolean
  restartRequired: boolean
  status: string
  settings: PluginSetting[]
  canConfigure: boolean
  hasAccount: boolean
  accountConnected: boolean
  websiteUrl?: string | null
  accountHosts?: string[] | null
}
export interface BackendOperation {
  id: string
  kind: string
  state: string
  message: string
  updatedAt: string
  metadataResult?: number | null
}
export interface ManualGame {
  ownershipId: number
  releaseId: number
  workId: number
  title: string
  igdbId?: number | null
  steamAppId?: string | null
  firstReleaseYear?: number | null
  platformLabel?: string | null
  executablePath?: string | null
  installPath?: string | null
  revision: string
  igdbMappingRevision: number
}
export interface HiddenGame {
  workId: number
  title: string
  hiddenAt: string
  storeEntryCount: number
}
export interface IdentityReview {
  revision: string
  hasCompletedSweep: boolean
  candidates: { id: number; leftReleaseId: number; rightReleaseId: number; score: number; status: string }[]
  workspace: Workspace & { releases: { id: number; workId: number }[] }
}

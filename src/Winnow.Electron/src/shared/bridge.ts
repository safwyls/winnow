/** Public renderer boundary. Discovery credentials and arbitrary filesystem access never cross it. */
export interface ApiRequest {
  route: string
  requestId?: string
  params?: Record<string, string | number>
  body?: unknown
}
export interface LinkOpenResult { opened: boolean; message?: string }
export interface ApiResult<T = unknown> {
  ok: boolean
  status: number
  data?: T
  message?: string
}
export interface ArtworkImport {
  workId: number
  slot: 'Hero' | 'Cover' | 'Icon'
  revision: string
}
export interface ArtworkSaveResult { success: boolean; message: string }
export interface ConnectionState {
  connected: boolean
  message: string
  epoch?: string
}
export interface BackendEvent {
  kind: string
  resource?: string
  epoch?: string
  sequence?: number
}
export interface ThemePackage {
  id: string
  name: string
  version: string
  apiVersion: number
  description?: string
  entry: string
  css?: string
}
export interface SteamSignInOptions {
  consentGranted: boolean
  staySignedIn: boolean
  capturePurchaseHistory?: boolean
  maxLoadMoreClicks?: number
  maxLicensesPages?: number
}
export interface SteamCapturedPages {
  licensesHtml?: string | null
  additionalLicensesHtml: string[]
  historyHtml?: string | null
  capturedAt: string
  source: 0
  steamId: string | null
}
export interface SteamCaptureResult {
  captureOutcome?: 'captured' | 'partial' | 'cancelled' | 'unavailable' | 'failed' | 'no-session'
  licensesStoppedBecause?: 'exhausted' | 'cap' | 'stalled' | 'interrupted' | 'failed'
  historyStoppedBecause?: 'exhausted' | 'cap' | 'stalled' | 'interrupted' | 'failed'
  /** Further licence pages followed after the initial page. */
  licensesPagesWalked?: number
  loadMoreClicks?: number
  pages?: SteamCapturedPages
  captureDetail?: string
  licensesTruncated?: boolean
  historyTruncated?: boolean
}
export interface SteamSignInResult extends SteamCaptureResult {
  signedIn: boolean
  outcome?: number
  steamId?: string | null
  expiresAt?: string | null
  health?: number
  accountConfirmed?: boolean
  persisted?: boolean
  refreshTokenCaptured?: boolean
  detail?: string | null
}
export interface ApplicationInfo {
  version: string
  platform: string
  packaged: boolean
  autostartSupported: boolean
  openAtLogin: boolean
  steamStoreAvailable?: boolean
}
export type ApplicationActivation =
  | { kind: 'show' }
  | { kind: 'fullscreen' }
  | { kind: 'game'; ownershipId: number }
  | { kind: 'plugin'; pluginId: string; releaseTag: string }
export interface WinnowBridge {
  appearanceSession?(): Promise<import('./appearance-session').AppearanceSession | null>
  importArtwork?(input: ArtworkImport): Promise<ApiResult<ArtworkSaveResult> | null>
  prepareEpicSignIn?(): Promise<import('./epic').EpicSignInPreparation | null>
  epicSignIn?(options: import('./epic').EpicSignInOptions): Promise<import('./epic').EpicSignInResult>
  openEpicSignInInBrowser?(options: import('./epic').EpicSignInOptions): Promise<void>
  completeEpicSignIn?(options: import('./epic').EpicSignInOptions & { callback: string }): Promise<import('./epic').EpicSignInResult>
  cancelEpicSignIn?(): Promise<boolean>
  request<T = unknown>(request: ApiRequest): Promise<ApiResult<T>>
  cancelRequest?(requestId: string): Promise<boolean>
  connection(): Promise<ConnectionState>
  onEvent(callback: (event: BackendEvent) => void): () => void
  onConnection(callback: (state: ConnectionState) => void): () => void
  artwork(provider: string, id: string, width?: number, requestId?: string): Promise<string | null>
  loadPreferences(): Promise<unknown>
  savePreferences(value: unknown): Promise<void>
  importProfile(): Promise<unknown | null>
  exportProfile(value: unknown): Promise<boolean>
  listThemes(): Promise<ThemePackage[]>
  listAvalonThemes?(): Promise<import('./avalonThemeDocument').AvalonThemeCatalogue>
  exportAvalonTheme?(text: string): Promise<{ file: string | null; diagnostics: import('./avalonThemeDocument').AvalonThemeDiagnostic[] }>
  onAvalonThemesChanged?(callback: () => void): () => void
  listFonts?(): Promise<string[]>
  windowAppearance?(value: import('./windowAppearance').WindowAppearanceRequest): Promise<import('./windowAppearance').WindowAppearanceResult>
  onWindowAppearanceInvalidated?(callback: () => void): () => void
  installTheme(): Promise<ThemePackage | null>
  setFullscreen(value: boolean): Promise<void>
  isFullscreen(): Promise<boolean>
  onFullscreen(callback: (value: boolean) => void): () => void
  openExternal(url: string): Promise<LinkOpenResult>
  quit?(): Promise<void>
  restartBackend?(): Promise<void>
  updateSnapshot?(): Promise<ApplicationUpdateSnapshot>
  updateAction?(action: ApplicationUpdateAction, value?: boolean): Promise<ApplicationUpdateSnapshot>
  onUpdate?(callback: (snapshot: ApplicationUpdateSnapshot) => void): () => void
  takeActivations?(): Promise<ApplicationActivation[]>
  onActivation?(callback: (activation: ApplicationActivation) => void): () => void
  openDataFolder?(folder: 'logs' | 'plugins' | 'themes'): Promise<void>
  openInstallFolder?(ownershipId: number): Promise<void>
  chooseManualExecutable?(): Promise<string | null>
  chooseManualExecutableFacts?(): Promise<import('./executable-facts').ExecutableFacts | null>
  exportAcquisitions?(): Promise<boolean>
  steamSignIn?(options: SteamSignInOptions): Promise<SteamSignInResult>
  cancelSteamWindow?(): Promise<boolean>
  steamCapturePages?(options: { consentGranted: boolean }): Promise<SteamCaptureResult>
  applicationInfo?(): Promise<ApplicationInfo>
  setOpenAtLogin?(enabled: boolean): Promise<void>
  notifySessionEnded?(value: { sessionId: number; title: string }): Promise<boolean>
  onJournalNotificationActivated?(callback: (sessionId: number) => void): () => void
  clearJournalNotification?(sessionId: number): Promise<void>
}
export type ApplicationUpdateAction =
  | 'check'
  | 'download'
  | 'restart'
  | 'update-and-restart'
  | 'cancel'
  | 'automatic'
  | 'beta'
  | 'manual-download'
export interface ApplicationUpdateSnapshot {
  automatic: boolean
  includeBeta: boolean
  busy: boolean
  canDownload: boolean
  canRestart: boolean
  canCancel: boolean
  progress: number
  status: string
  availableVersion?: string | null
  releaseUrl?: string | null
  downloadUrl?: string | null
  recoveryStatus?: string | null
}
declare global {
  interface Window {
    winnow: WinnowBridge
  }
}

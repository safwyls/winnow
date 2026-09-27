/** Public renderer boundary. Discovery credentials and arbitrary filesystem access never cross it. */
export interface ApiRequest {
  route: string
  params?: Record<string, string | number>
  body?: unknown
}
export interface ApiResult<T = unknown> {
  ok: boolean
  status: number
  data?: T
  message?: string
}
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
export interface WinnowBridge {
  request<T = unknown>(request: ApiRequest): Promise<ApiResult<T>>
  connection(): Promise<ConnectionState>
  onEvent(callback: (event: BackendEvent) => void): () => void
  onConnection(callback: (state: ConnectionState) => void): () => void
  artwork(provider: string, id: string, width?: number): Promise<string | null>
  loadPreferences(): Promise<unknown>
  savePreferences(value: unknown): Promise<void>
  importProfile(): Promise<unknown | null>
  exportProfile(value: unknown): Promise<boolean>
  listThemes(): Promise<ThemePackage[]>
  installTheme(): Promise<ThemePackage | null>
  setFullscreen(value: boolean): Promise<void>
  isFullscreen(): Promise<boolean>
  onFullscreen(callback: (value: boolean) => void): () => void
  openExternal(url: string): Promise<void>
}
declare global {
  interface Window {
    winnow: WinnowBridge
  }
}

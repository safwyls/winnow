import { contextBridge, ipcRenderer } from 'electron'
import type {
  ApplicationActivation,
  ApplicationUpdateSnapshot,
  BackendEvent,
  ConnectionState,
  WinnowBridge,
} from '../shared/bridge'

function subscribe<T>(channel: string, callback: (value: T) => void): () => void {
  if (typeof callback !== 'function') throw new Error('A callback is required')
  const listener = (_event: Electron.IpcRendererEvent, value: T) => callback(value)
  ipcRenderer.on(channel, listener)
  return () => ipcRenderer.removeListener(channel, listener)
}
const bridge: WinnowBridge = {
  importArtwork: (input) => ipcRenderer.invoke('winnow:artwork:import', input),
  request: (request) => ipcRenderer.invoke('winnow:request', request),
  cancelRequest: (requestId) => ipcRenderer.invoke('winnow:request:cancel', requestId),
  connection: () => ipcRenderer.invoke('winnow:connection'),
  onEvent: (callback) => subscribe<BackendEvent>('winnow:event', callback),
  onConnection: (callback) => subscribe<ConnectionState>('winnow:connection', callback),
  artwork: (provider, id, width, requestId) => ipcRenderer.invoke('winnow:artwork', provider, id, width, requestId),
  loadPreferences: () => ipcRenderer.invoke('winnow:preferences:load'),
  appearanceSession: () => ipcRenderer.invoke('winnow:appearance:session'),
  savePreferences: (value) => ipcRenderer.invoke('winnow:preferences:save', value),
  importProfile: () => ipcRenderer.invoke('winnow:profile:import'),
  exportProfile: (value) => ipcRenderer.invoke('winnow:profile:export', value),
  listThemes: () => ipcRenderer.invoke('winnow:themes:list'),
  listAvalonThemes: () => ipcRenderer.invoke('winnow:avalon-themes:list'),
  exportAvalonTheme: (text) => ipcRenderer.invoke('winnow:avalon-themes:export', text),
  onAvalonThemesChanged: (callback) => subscribe('winnow:avalon-themes:changed', callback),
  listFonts: () => ipcRenderer.invoke('winnow:fonts'),
  windowAppearance: (value) => ipcRenderer.invoke('winnow:window:appearance', value),
  onWindowAppearanceInvalidated: (callback) => subscribe('winnow:window:appearance:invalidated', callback),
  installTheme: () => ipcRenderer.invoke('winnow:themes:install'),
  setFullscreen: (value) => ipcRenderer.invoke('winnow:fullscreen', value),
  isFullscreen: () => ipcRenderer.invoke('winnow:fullscreen:get'),
  onFullscreen: (callback) => subscribe<boolean>('winnow:fullscreen:changed', callback),
  openExternal: (url) => ipcRenderer.invoke('winnow:external', url),
  quit: () => ipcRenderer.invoke('winnow:quit'),
  restartBackend: () => ipcRenderer.invoke('winnow:backend:restart'),
  updateSnapshot: () => ipcRenderer.invoke('winnow:update:snapshot'),
  updateAction: (action, value) => ipcRenderer.invoke('winnow:update:action', action, value),
  onUpdate: (callback) => subscribe<ApplicationUpdateSnapshot>('winnow:update:changed', callback),
  takeActivations: () => ipcRenderer.invoke('winnow:activation:pending'),
  onActivation: (callback) => subscribe<ApplicationActivation>('winnow:activation', callback),
  openDataFolder: (folder) => ipcRenderer.invoke('winnow:folder', folder),
  openInstallFolder: (ownershipId) => ipcRenderer.invoke('winnow:install-folder', ownershipId),
  chooseManualExecutable: () => ipcRenderer.invoke('winnow:manual-executable'),
  chooseManualExecutableFacts: () => ipcRenderer.invoke('winnow:manual-executable-facts'),
  exportAcquisitions: () => ipcRenderer.invoke('winnow:acquisitions:export'),
  steamSignIn: (options) => ipcRenderer.invoke('winnow:steam:signin', options),
  prepareEpicSignIn: () => ipcRenderer.invoke('winnow:epic:prepare'),
  epicSignIn: (options) => ipcRenderer.invoke('winnow:epic:signin', options),
  openEpicSignInInBrowser: (options) => ipcRenderer.invoke('winnow:epic:browser', options),
  completeEpicSignIn: (options) => ipcRenderer.invoke('winnow:epic:complete', options),
  cancelEpicSignIn: () => ipcRenderer.invoke('winnow:epic:cancel'),
  cancelSteamWindow: () => ipcRenderer.invoke('winnow:steam:cancel'),
  steamCapturePages: (options) => ipcRenderer.invoke('winnow:steam:capture', options),
  applicationInfo: () => ipcRenderer.invoke('winnow:application:info'),
  setOpenAtLogin: (enabled) => ipcRenderer.invoke('winnow:application:autostart', enabled),
  notifySessionEnded: (value) => ipcRenderer.invoke('winnow:journal:notify', value),
  onJournalNotificationActivated: (callback) => subscribe<number>('winnow:journal:activated', callback),
  clearJournalNotification: (sessionId) => ipcRenderer.invoke('winnow:journal:clear', sessionId),
}
contextBridge.exposeInMainWorld('winnow', Object.freeze(bridge))

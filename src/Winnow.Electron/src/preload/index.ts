import { contextBridge, ipcRenderer } from 'electron'
import type { BackendEvent, ConnectionState, WinnowBridge } from '../shared/bridge'

function subscribe<T>(channel: string, callback: (value: T) => void): () => void {
  if (typeof callback !== 'function') throw new Error('A callback is required')
  const listener = (_event: Electron.IpcRendererEvent, value: T) => callback(value)
  ipcRenderer.on(channel, listener)
  return () => ipcRenderer.removeListener(channel, listener)
}
const bridge: WinnowBridge = {
  request: (request) => ipcRenderer.invoke('winnow:request', request),
  connection: () => ipcRenderer.invoke('winnow:connection'),
  onEvent: (callback) => subscribe<BackendEvent>('winnow:event', callback),
  onConnection: (callback) => subscribe<ConnectionState>('winnow:connection', callback),
  artwork: (provider, id, width) => ipcRenderer.invoke('winnow:artwork', provider, id, width),
  loadPreferences: () => ipcRenderer.invoke('winnow:preferences:load'),
  savePreferences: (value) => ipcRenderer.invoke('winnow:preferences:save', value),
  importProfile: () => ipcRenderer.invoke('winnow:profile:import'),
  exportProfile: (value) => ipcRenderer.invoke('winnow:profile:export', value),
  listThemes: () => ipcRenderer.invoke('winnow:themes:list'),
  installTheme: () => ipcRenderer.invoke('winnow:themes:install'),
  setFullscreen: (value) => ipcRenderer.invoke('winnow:fullscreen', value),
  isFullscreen: () => ipcRenderer.invoke('winnow:fullscreen:get'),
  onFullscreen: (callback) => subscribe<boolean>('winnow:fullscreen:changed', callback),
  openExternal: (url) => ipcRenderer.invoke('winnow:external', url),
}
contextBridge.exposeInMainWorld('winnow', Object.freeze(bridge))

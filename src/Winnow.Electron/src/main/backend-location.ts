import { join, resolve } from 'node:path'

/** Primary releases retain backend/ beside the app; secondary builder packages use resources/backend/. */
export function bundledBackendPaths(resourcesPath: string, platform: string = process.platform): string[] {
  const name = platform === 'win32' ? 'Winnow.Backend.exe' : 'Winnow.Backend'
  return [resolve(resourcesPath, '..', 'backend', name), join(resourcesPath, 'backend', name)]
}

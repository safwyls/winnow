import { existsSync, readdirSync } from 'node:fs'
import { join } from 'node:path'

/** Reuse prototype profiles in place when the packaged product name becomes Winnow. */
export function frontendDataLocation(appData: string, current: string, hasProfiles = containsProfiles): string {
  if (hasProfiles(current)) return current
  for (const name of ['Winnow Afterglow', 'winnow-electron']) {
    const legacy = join(appData, name)
    if (hasProfiles(legacy)) return legacy
  }
  return current
}

function containsProfiles(directory: string): boolean {
  try {
    const libraries = join(directory, 'libraries')
    return readdirSync(libraries, { withFileTypes: true }).some(
      (entry) => entry.isDirectory() && existsSync(join(libraries, entry.name, 'preferences.json')),
    )
  } catch {
    return false
  }
}

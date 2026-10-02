import { closeSync, existsSync, fstatSync, openSync, readFileSync, unlinkSync, writeFileSync } from 'node:fs'
import { isAbsolute, join, resolve } from 'node:path'
import { releaseVersion } from './update-policy'

interface Resume {
  version: string
  created: number
  dataDirectory?: string
  noSync: boolean
}
export const updateResumeFile = (appData: string, appName: string) =>
  join(appData, `${appName}-update-resume.json`)

/** A replacement restarts the selected library, never the previous launch's one-shot commands. */
export function restartArguments(dataDirectory: string, originalArgs: readonly string[]): string[] {
  if (
    typeof dataDirectory !== 'string' ||
    !dataDirectory.trim() ||
    dataDirectory.includes('\0') ||
    dataDirectory.length > 4096
  )
    throw new Error('Invalid update data directory.')
  return ['--data-dir', resolve(dataDirectory), ...(originalArgs.includes('--no-sync') ? ['--no-sync'] : [])]
}

export function prepareUpdateResume(path: string, value: Omit<Resume, 'created'>, now = Date.now()): void {
  if (
    typeof value.version !== 'string' ||
    !releaseVersion(value.version) ||
    typeof value.noSync !== 'boolean' ||
    !Number.isSafeInteger(now) ||
    now < 0 ||
    (value.dataDirectory !== undefined &&
      (typeof value.dataDirectory !== 'string' ||
        !isAbsolute(value.dataDirectory) ||
        value.dataDirectory.length > 4096 ||
        value.dataDirectory.includes('\0')))
  )
    throw new Error('Invalid update restart context.')
  const json = JSON.stringify({
    version: value.version,
    dataDirectory: value.dataDirectory,
    noSync: value.noSync,
    created: now,
  })
  if (Buffer.byteLength(json, 'utf8') > 8192) throw new Error('Invalid update restart context.')
  writeFileSync(path, json, {
    encoding: 'utf8',
    mode: 0o600,
    flag: 'w',
  })
}
export function clearUpdateResume(path: string): void {
  try {
    unlinkSync(path)
  } catch (failure) {
    if ((failure as NodeJS.ErrnoException).code !== 'ENOENT') throw failure
  }
}
/** The installer only forwards --updated (AppImage forwards an inherited flag), never arbitrary startup arguments. */
export function restoreUpdateResume(
  path: string,
  options: { version: string; args: string[]; appImageUpdated: boolean; now?: number },
): { args: string[]; recoveryStatus?: string } {
  const args = [...options.args]
  if (!args.includes('--updated') && !options.appImageUpdated) return { args }
  if (!existsSync(path)) return { args }
  let descriptor: number | undefined
  try {
    descriptor = openSync(path, 'r')
    if (fstatSync(descriptor).size > 8192) throw new Error('Oversized restart context.')
    const value = JSON.parse(readFileSync(descriptor, 'utf8')) as Resume
    const age = (options.now ?? Date.now()) - value.created
    if (
      !value ||
      !releaseVersion(value.version) ||
      value.version !== options.version ||
      !Number.isSafeInteger(value.created) ||
      value.created < 0 ||
      !Number.isFinite(age) ||
      age < 0 ||
      age > 2 * 60 * 60_000 ||
      typeof value.noSync !== 'boolean' ||
      (value.dataDirectory !== undefined &&
        (typeof value.dataDirectory !== 'string' ||
          !isAbsolute(value.dataDirectory) ||
          value.dataDirectory.length > 4096 ||
          value.dataDirectory.includes('\0')))
    )
      throw new Error('Invalid restart context.')
    if (
      args.some(
        (arg) =>
          arg === '--data-dir' ||
          arg.startsWith('--data-dir=') ||
          /^winnow:/i.test(arg) ||
          arg.startsWith('--uri'),
      )
    )
      throw new Error('Conflicting update startup arguments.')
    // Native installers supply only their marker and apphost. Do not replay injected or inherited commands.
    const restored = args[0] && !args[0].startsWith('--') ? [args[0]] : []
    if (args.includes('--updated')) restored.push('--updated')
    if (value.dataDirectory) restored.push('--data-dir', value.dataDirectory)
    if (value.noSync) restored.push('--no-sync')
    return { args: restored }
  } catch {
    // Refuse startup: silently losing an isolated directory would open the user's real library.
    throw new Error(
      'The update restart context could not be restored. Reopen Winnow using your previous --data-dir option.',
    )
  } finally {
    if (descriptor !== undefined) closeSync(descriptor)
    clearUpdateResume(path)
  }
}

import { closeSync, existsSync, fstatSync, openSync, readFileSync, unlinkSync, writeFileSync } from 'node:fs'
import { isAbsolute, join } from 'node:path'
import { releaseVersion } from './update-policy'

interface Resume {
  version: string
  created: number
  dataDirectory?: string
  noSync: boolean
}
export const updateResumeFile = (appData: string, appName: string) =>
  join(appData, `${appName}-update-resume.json`)
export function prepareUpdateResume(path: string, value: Omit<Resume, 'created'>, now = Date.now()): void {
  if (
    !releaseVersion(value.version) ||
    (value.dataDirectory !== undefined &&
      (!isAbsolute(value.dataDirectory) || value.dataDirectory.includes('\0')))
  )
    throw new Error('Invalid update restart context.')
  writeFileSync(path, JSON.stringify({ ...value, created: now }), {
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
    if (value.dataDirectory) args.push('--data-dir', value.dataDirectory)
    if (value.noSync && !args.includes('--no-sync')) args.push('--no-sync')
    return { args }
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

import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

/** Capture identity while building; installed applications do not need a Git checkout. */
export function buildInformationalVersion(packageDirectory: string): string {
  const { version } = JSON.parse(readFileSync(join(packageDirectory, 'package.json'), 'utf8')) as {
    version: unknown
  }
  if (typeof version !== 'string' || !version.trim()) throw new Error('The frontend package needs a version.')
  let commit: string | undefined
  try {
    const revision = execFileSync('git', ['rev-parse', 'HEAD'], {
      cwd: packageDirectory,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
      timeout: 5000,
      windowsHide: true,
    }).trim()
    if (/^(?:[a-f0-9]{40}|[a-f0-9]{64})$/i.test(revision)) commit = revision
  } catch {
    // Source archives and builds without Git retain their package version.
  }
  return commit ? `${version.split('+', 1)[0]}+${commit}` : version
}

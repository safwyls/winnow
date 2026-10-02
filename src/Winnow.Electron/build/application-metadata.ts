import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

/** Capture identity while building; installed applications do not need a Git checkout. */
export function buildInformationalVersion(
  packageDirectory: string,
  environment: NodeJS.ProcessEnv = process.env,
): string {
  const releaseVersion = environment.WINNOW_BUILD_VERSION
  const releaseCommit = environment.WINNOW_BUILD_COMMIT
  if (releaseVersion !== undefined || releaseCommit !== undefined) {
    if (
      !releaseVersion ||
      !/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/.test(
        releaseVersion,
      ) ||
      releaseVersion
        .split('-')[0]
        .split('.')
        .some((part) => Number(part) > 65535) ||
      releaseVersion
        .slice(releaseVersion.indexOf('-') + 1)
        .split('.')
        .some((part) => /^0\d+$/.test(part)) ||
      !releaseCommit ||
      !/^[a-f0-9]{40}$/i.test(releaseCommit)
    )
      throw new Error('Release builds require a valid version and a complete source commit.')
    return `${releaseVersion}+${releaseCommit.toLowerCase()}`
  }
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

import { appendFileSync, existsSync, mkdirSync, renameSync, statSync } from 'node:fs'
import { isAbsolute, join } from 'node:path'
import { randomUUID } from 'node:crypto'
import { constants } from 'node:os'
import { applicationBuildInfo } from './application-build-info'

export const startupFailureCode = 3
export const dataDirectoryRefusalCode = 2
export const startupFailureTitle = 'Winnow could not start'
const diagnosticRun = randomUUID().replaceAll('-', '')
const chromiumFailureCodes = new Set([
  'ERR_FAILED',
  'ERR_ABORTED',
  'ERR_CONNECTION_REFUSED',
  'ERR_CONNECTION_RESET',
  'ERR_NAME_NOT_RESOLVED',
  'ERR_TIMED_OUT',
])

/** Same privacy boundary as the backend diagnostic formatter, including pre-logger failures. */
export function scrubStartupDiagnostic(message: string, username?: string) {
  let text = message
    .slice(0, 32768)
    .replace(
      /(?:https?:\/\/|file:\/{2,3}|[A-Za-z]:[\\/]|\\\\|(?<!\w)\/(?:[^\s/]+\/)?)[^\r\n]*/g,
      '[redacted-path]',
    )
    .replace(/\b7656119\d{10}\b|\bSTEAM_[0-5]:[01]:\d+\b|\[U:1:\d+\]/gi, '[redacted-id]')
    .replace(
      /(?:\b(?:access[_-]?token|refresh[_-]?token|token|api[_-]?key|secret|password|authorization|cookie|code)\b\s*[:=]\s*|\b(?:Bearer|Basic)\s+)[^\r\n]*|\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/gi,
      '[redacted-secret]',
    )
    .replace(/\b(?:account|username|user|steam3id|steamid)\s*[:=]\s*[^\r\n]*/gi, '[redacted-account]')
    .replace(/[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}/g, '[redacted-account]')
  if (username)
    text = text.replace(new RegExp(username.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi'), '[redacted-user]')
  return text
}

export function isStartupCancellation(error: unknown) {
  return (
    error instanceof Error &&
    (error.name === 'AbortError' || (error as NodeJS.ErrnoException).code === 'ABORT_ERR')
  )
}

export function startupFailureSentence(error: unknown, directory?: string) {
  const name = error instanceof Error && /^[A-Za-z][A-Za-z0-9]{0,80}$/.test(error.name) ? error.name : 'Error'
  const message = error instanceof Error ? error.message : String(error ?? 'Startup failed.')
  const location = directory?.trim()
    ? `Your library is at ${directory}. Check the diagnostic log in its logs folder for details.`
    : 'Winnow did not finish selecting its data directory. Check the application configuration and try again.'
  return `Winnow could not start.\n\n${name}: ${scrubStartupDiagnostic(message, process.env.USERNAME ?? process.env.USER)}\n\n${location}`
}

/** Keep only exception metadata; the human-facing message is never a log template. */
export function startupDiagnostic(error: unknown) {
  const build = applicationBuildInfo(false)
  const version = /^\d+(?:\.\d+){1,3}(?:-[A-Za-z0-9.-]{1,40})?$/.test(build.version)
    ? build.version
    : 'unknown'
  const commit = /^[0-9a-f]{7,64}$/i.test(build.commit) ? build.commit : 'unknown'
  const fields = [
    `run=${diagnosticRun}`,
    `build=${version}`,
    `commit=${commit}`,
    `os=${process.platform}`,
    `arch=${process.arch}`,
    `runtime=${process.versions.node}`,
  ]
  if (error instanceof Error) {
    const type = error.constructor.name
    fields.push(
      `type=${/^(?:Error|[A-Za-z_$][A-Za-z0-9_$]{0,80}(?:Error|Exception))$/.test(type) ? type : 'Error'}`,
    )
    const native = error as NodeJS.ErrnoException
    if (
      typeof native.code === 'string' &&
      (Object.hasOwn(constants.errno, native.code) || chromiumFailureCodes.has(native.code))
    )
      fields.push(`code=${native.code}`)
    // Node/Chromium error numbers are useful native facts; JavaScript has no .NET HResult.
    if (Number.isSafeInteger(native.errno)) fields.push(`errno=${native.errno}`)

    // Remove the complete message first, including embedded newlines. Only named
    // V8 frames survive; file locations and unnamed frames are omitted.
    const header = `${error.name}: ${error.message}`
    const stack = error.stack
    if (stack?.startsWith(header)) {
      const frames = stack
        .slice(header.length, header.length + 32768)
        .split('\n')
        .flatMap((line) => {
          const match = /^\s+at (?:async )?([A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*) \(/.exec(line)
          return match && match[1].length <= 160 ? [match[1]] : []
        })
        .slice(0, 6)
      if (frames.length) fields.push(`at=${frames.join(',')}`)
    }
  } else fields.push('type=Unknown')
  return `${new Date().toISOString()} StartupFailure ${fields.join(' ')}`
}

function writeStartupDiagnostic(text: string, directory?: string) {
  if (!directory || !isAbsolute(directory) || !existsSync(directory)) return
  const logs = join(directory, 'logs')
  mkdirSync(logs, { recursive: true })
  const path = join(logs, 'electron-startup-failure.log')
  if (existsSync(path) && statSync(path).size >= 512 * 1024) renameSync(path, `${path}.previous`)
  appendFileSync(path, `${text}\n`, 'utf8')
}

/** Logging and alert failures must not replace the original exit status. */
export function reportStartupFailure(
  error: unknown,
  options: {
    directory?: string
    exitCode?: number
    surface: (title: string, text: string) => void
    log?: (text: string, directory?: string) => void
  },
) {
  if (isStartupCancellation(error)) return 0
  const text = startupFailureSentence(error, options.directory)
  try {
    ;(options.log ?? writeStartupDiagnostic)(startupDiagnostic(error), options.directory)
  } catch {
    /* The alert remains available. */
  }
  try {
    options.surface(startupFailureTitle, text)
  } catch {
    /* The exit status is the remaining failure signal. */
  }
  return options.exitCode ?? startupFailureCode
}

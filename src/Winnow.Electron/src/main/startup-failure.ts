import { appendFileSync, existsSync, mkdirSync, renameSync, statSync } from 'node:fs'
import { isAbsolute, join } from 'node:path'

export const startupFailureCode = 3
export const dataDirectoryRefusalCode = 2
export const startupFailureTitle = 'Winnow could not start'

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

function writeStartupDiagnostic(text: string, directory?: string) {
  if (!directory || !isAbsolute(directory) || !existsSync(directory)) return
  const logs = join(directory, 'logs')
  mkdirSync(logs, { recursive: true })
  const path = join(logs, 'electron-startup-failure.log')
  if (existsSync(path) && statSync(path).size >= 512 * 1024) renameSync(path, `${path}.previous`)
  appendFileSync(
    path,
    `${new Date().toISOString()} ${scrubStartupDiagnostic(text).replace(/[\r\n]+/g, ' ')}\n`,
    'utf8',
  )
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
    ;(options.log ?? writeStartupDiagnostic)(text, options.directory)
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

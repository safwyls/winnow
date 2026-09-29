import { afterEach, describe, expect, it, vi } from 'vitest'
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import {
  dataDirectoryRefusalCode,
  isStartupCancellation,
  reportStartupFailure,
  scrubStartupDiagnostic,
  startupFailureCode,
  startupFailureSentence,
  startupFailureTitle,
} from '../src/main/startup-failure'
import { dataDirectoryArgument } from '../src/main/lifecycle'

const directories: string[] = []
afterEach(() => {
  for (const directory of directories.splice(0)) rmSync(directory, { recursive: true, force: true })
})
function temporaryDirectory() {
  const directory = mkdtempSync(join(tmpdir(), 'winnow-startup-'))
  directories.push(directory)
  return directory
}

describe('startup failure boundary migrated from Avalonia', () => {
  it('surfaces the startup fault once with a nonzero exit status', () => {
    const surface = vi.fn()
    expect(reportStartupFailure(new Error('the migration runner gave up'), { surface, log: vi.fn() })).toBe(3)
    expect(surface).toHaveBeenCalledExactlyOnceWith(
      startupFailureTitle,
      expect.stringContaining('the migration runner gave up'),
    )
  })
  it('names the failure type, message and selected library separately', () => {
    const error = new TypeError('the migration runner gave up')
    const sentence = startupFailureSentence(error, 'C:\\throwaway\\winnow')
    expect(sentence).toContain('TypeError: the migration runner gave up')
    expect(sentence).toContain('C:\\throwaway\\winnow')
  })
  it('explains that the data directory is unresolved without a dangling location', () => {
    const sentence = startupFailureSentence(new Error('no'), '')
    expect(sentence).toContain('its data directory')
    expect(sentence).not.toContain('at .')
  })
  it.each(['AbortError', 'ABORT_ERR'])(
    'treats %s cancellation as a clean shutdown without logging or showing an alert',
    (kind) => {
      const error =
        kind === 'AbortError'
          ? new DOMException('Closed', 'AbortError')
          : Object.assign(new Error('Closed'), { code: kind })
      const surface = vi.fn(),
        log = vi.fn()
      expect(isStartupCancellation(error)).toBe(true)
      expect(reportStartupFailure(error, { surface, log })).toBe(0)
      expect(surface).not.toHaveBeenCalled()
      expect(log).not.toHaveBeenCalled()
    },
  )
  it('surfaces failures before the logger or a data directory exists', () => {
    const surface = vi.fn()
    expect(reportStartupFailure(new Error('the container would not build'), { surface })).toBe(3)
    expect(surface).toHaveBeenCalledOnce()
  })
  it('preserves the exit status when both logging and the alert throw', () => {
    expect(
      reportStartupFailure(new Error('first'), {
        log: () => {
          throw Error('logger disposed')
        },
        surface: () => {
          throw Error('no message box here')
        },
      }),
    ).toBe(startupFailureCode)
  })
  it('keeps startup faults distinct from invalid data-directory refusals', () => {
    expect(startupFailureCode).toBe(3)
    expect(dataDirectoryRefusalCode).toBe(2)
    expect(
      reportStartupFailure(new Error('invalid directory'), {
        exitCode: dataDirectoryRefusalCode,
        surface: vi.fn(),
        log: vi.fn(),
      }),
    ).toBe(2)
  })
  it('redacts configuration secrets before a logger exists', () => {
    const surface = vi.fn()
    reportStartupFailure(new Error('Invalid setting: api_key=private-review-secret'), { surface })
    const sentence = surface.mock.calls[0][1]
    expect(sentence).toContain('[redacted-secret]')
    expect(sentence).not.toContain('private-review-secret')
    expect(sentence).not.toContain('has not been changed')
  })
  it('preserves the original fault when logger construction fails', () => {
    const surface = vi.fn()
    reportStartupFailure(new Error('original failure'), {
      surface,
      log: () => {
        throw Error('logger construction failed')
      },
    })
    expect(surface).toHaveBeenCalledWith(startupFailureTitle, expect.stringContaining('original failure'))
    expect(surface.mock.calls[0][1]).not.toContain('logger construction failed')
  })
  it.each([
    ['token=secret-value', 'secret-value'],
    ['Bearer secret-value', 'secret-value'],
    ['open C:\\Users\\someone\\config.json', 'someone'],
    ['https://example.test/?code=secret', 'example.test'],
    ['account=private-account', 'private-account'],
    ['private.person@example.test', 'private.person'],
    ['Steam 76561198000000001', '76561198000000001'],
    ['STEAM_0:1:12345', '12345'],
  ])('scrubs sensitive startup diagnostics: %s', (input, secret) => {
    expect(scrubStartupDiagnostic(input)).not.toContain(secret)
    expect(scrubStartupDiagnostic(input)).toContain('[redacted-')
  })
  it('escapes username metacharacters and bounds diagnostic text', () => {
    expect(scrubStartupDiagnostic('user A.name+ says no', 'A.name+')).toBe('user [redacted-user] says no')
    expect(scrubStartupDiagnostic('x'.repeat(40000))).toHaveLength(32768)
  })
  it('writes bounded redacted logs inside the selected throwaway directory', () => {
    const directory = temporaryDirectory()
    const surface = vi.fn()
    reportStartupFailure(new Error('token=private-token'), { directory, surface })
    const path = join(directory, 'logs', 'electron-startup-failure.log')
    const logged = readFileSync(path, 'utf8')
    expect(logged).toContain('[redacted-secret]')
    expect(logged).not.toContain('private-token')
    expect(logged).not.toContain(directory)
    writeFileSync(path, 'x'.repeat(512 * 1024))
    reportStartupFailure(new Error('second fault'), { directory, surface })
    expect(readFileSync(`${path}.previous`, 'utf8')).toHaveLength(512 * 1024)
    expect(readFileSync(path, 'utf8')).toContain('second fault')
  })
  it('does not create an unresolved library directory just to log a failure', () => {
    const path = join(temporaryDirectory(), 'missing')
    reportStartupFailure(new Error('early failure'), { directory: path, surface: vi.fn() })
    expect(existsSync(path)).toBe(false)
  })
})

describe('data directory argument contracts', () => {
  it('accepts both spellings and resolves relative paths', () => {
    const path = resolve('throwaway library')
    expect(dataDirectoryArgument(['--data-dir', 'throwaway library'])).toBe(path)
    expect(dataDirectoryArgument(['--data-dir=throwaway library'])).toBe(path)
  })
  it('does not invent an override when the argument is absent', () => {
    expect(dataDirectoryArgument([])).toBeUndefined()
    expect(dataDirectoryArgument(['--no-sync', '--seed-sample'])).toBeUndefined()
  })
  it.each([['--data-dir'], ['--data-dir', '--no-sync'], ['--data-dir='], ['--data-dir=   ']])(
    'refuses a missing path in %j',
    (...args) => {
      expect(() => dataDirectoryArgument(args)).toThrow('--data-dir')
    },
  )
})

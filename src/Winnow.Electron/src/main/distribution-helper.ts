import { execFile, spawn } from 'node:child_process'
import { promisify } from 'node:util'
import { join } from 'node:path'

export function distributionHelper(installation: string): string {
  return join(
    installation,
    'update-helper',
    `Winnow.Update.Helper${process.platform === 'win32' ? '.exe' : ''}`,
  )
}

export async function runDistributionHelper(
  helper: string,
  args: string[],
  signal?: AbortSignal,
): Promise<string> {
  const result = await promisify(execFile)(helper, args, {
    windowsHide: true,
    timeout: 120_000,
    maxBuffer: 64 * 1024,
    signal,
  })
  return result.stdout.trim()
}

export interface DistributionLease {
  canUpdate: boolean
  recoveryStatus?: string
  ready(): Promise<void>
  dispose(): void
}

/** The companion holds the native installation lease until this frontend closes. */
export function startDistributionLease(options: {
  helper: string
  installation: string
  dataDirectory: string
  signal: AbortSignal
  onLost(error: Error): void
  launch?: typeof spawn
}): Promise<DistributionLease> {
  options.signal.throwIfAborted()
  const child = (options.launch ?? spawn)(
    options.helper,
    [
      'frontend',
      '--installation',
      options.installation,
      '--data-dir',
      options.dataDirectory,
      '--pid',
      String(process.pid),
    ],
    { stdio: 'pipe', windowsHide: true },
  )
  return new Promise((resolve, reject) => {
    let buffer = '',
      diagnostic = '',
      leased = false,
      closed = false,
      acknowledged = false
    let readyPromise: Promise<void> | undefined
    let readyResolve: (() => void) | undefined, readyReject: ((error: Error) => void) | undefined
    let readyTimer: ReturnType<typeof setTimeout> | undefined
    const timer = setTimeout(() => fail(Error('The update recovery guard did not start.')), 20_000)
    const dispose = () => {
      if (closed) return
      closed = true
      clearTimeout(timer)
      clearTimeout(readyTimer)
      options.signal.removeEventListener('abort', abort)
      readyReject?.(Error('The update recovery guard closed.'))
      child.stdin?.end()
      child.kill()
    }
    const fail = (error: Error) => {
      if (closed) return
      const wasLeased = leased
      dispose()
      if (wasLeased) options.onLost(error)
      else reject(error)
    }
    const abort = () => {
      const wasLeased = leased
      dispose()
      if (!wasLeased) reject(new DOMException('Update startup was cancelled.', 'AbortError'))
    }
    options.signal.addEventListener('abort', abort, { once: true })
    if (options.signal.aborted) abort()
    child.once('error', fail)
    child.once('exit', () => fail(Error(diagnostic.trim() || 'The update recovery guard exited.')))
    child.stderr?.on('data', (chunk: Buffer) => {
      diagnostic += chunk.toString('utf8')
      if (Buffer.byteLength(diagnostic) > 16 * 1024) fail(Error('Invalid update recovery diagnostics.'))
    })
    child.stdout?.on('data', (chunk: Buffer) => {
      buffer += chunk.toString('utf8')
      if (Buffer.byteLength(buffer) > 16 * 1024) return fail(Error('Invalid update recovery response.'))
      for (;;) {
        const end = buffer.indexOf('\n')
        if (end < 0) break
        const line = buffer.slice(0, end)
        buffer = buffer.slice(end + 1)
        try {
          const frame = JSON.parse(line)
          if (
            !leased &&
            frame.kind === 'leased' &&
            typeof frame.canUpdate === 'boolean' &&
            (frame.recoveryStatus == null || typeof frame.recoveryStatus === 'string')
          ) {
            leased = true
            clearTimeout(timer)
            // Once acquired, keep replacement excluded while normal quit drains updater work.
            // The owner releases us at will-quit or on startup failure.
            options.signal.removeEventListener('abort', abort)
            resolve({
              canUpdate: frame.canUpdate,
              recoveryStatus: frame.recoveryStatus ?? undefined,
              dispose,
              ready() {
                if (acknowledged) return Promise.resolve()
                if (closed) return Promise.reject(Error('The update recovery guard closed.'))
                return (readyPromise ??= new Promise<void>((done, failed) => {
                  readyResolve = done
                  readyReject = failed
                  readyTimer = setTimeout(
                    () => fail(Error('The update startup acknowledgement timed out.')),
                    20_000,
                  )
                  child.stdin!.write(`${JSON.stringify({ kind: 'ready' })}\n`, (error) => {
                    if (error) fail(error)
                  })
                }))
              },
            })
          } else if (leased && frame.kind === 'ready' && readyResolve && !acknowledged) {
            acknowledged = true
            clearTimeout(readyTimer)
            readyResolve()
            readyReject = undefined
          } else throw Error('Invalid update recovery response.')
        } catch (error) {
          fail(error instanceof Error ? error : Error('Invalid update recovery response.'))
        }
      }
    })
  })
}

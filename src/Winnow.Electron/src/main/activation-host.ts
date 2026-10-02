import { spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { access } from 'node:fs/promises'
import { dirname, isAbsolute, join, resolve } from 'node:path'
import type { ApplicationActivation } from '../shared/bridge'
import { validatedActivation } from './activation'
import { BackendStartupFailure } from './startup-backend'
import { scrubStartupDiagnostic } from './startup-failure'
import { bundledBackendPaths } from './backend-location'

export interface ActivationHostLocation {
  appPath: string
  resourcesPath: string
  environment?: NodeJS.ProcessEnv
  platform?: string
  exists?: (path: string) => Promise<void>
}

/** The ownership helper must be a direct child; dotnet run would insert another parent process. */
export async function activationHostCommand(
  options: ActivationHostLocation,
): Promise<{ command: string; prefix: string[]; cwd: string }> {
  const environment = options.environment ?? process.env
  const exists = options.exists ?? access
  const explicit = environment.WINNOW_ACTIVATION_HELPER_PATH
  if (explicit && !isAbsolute(explicit))
    throw new Error('WINNOW_ACTIVATION_HELPER_PATH must be an absolute executable or DLL path.')
  const name = (options.platform ?? process.platform) === 'win32' ? 'Winnow.Backend.exe' : 'Winnow.Backend'
  const candidates = explicit
    ? [explicit]
    : [
        environment.WINNOW_BACKEND_PATH,
        ...bundledBackendPaths(options.resourcesPath, options.platform),
        resolve(options.appPath, '..', 'Winnow.Backend', 'bin', 'Debug', 'net10.0', name),
        resolve(options.appPath, '..', 'Winnow.Backend', 'bin', 'Release', 'net10.0', name),
      ]
  for (const candidate of candidates) {
    if (!candidate || !isAbsolute(candidate)) continue
    try {
      await exists(candidate)
      return {
        command: candidate.toLowerCase().endsWith('.dll') ? 'dotnet' : candidate,
        prefix: candidate.toLowerCase().endsWith('.dll') ? [candidate] : [],
        cwd: dirname(candidate),
      }
    } catch {}
  }
  throw new Error(
    explicit
      ? 'The explicitly configured WINNOW_ACTIVATION_HELPER_PATH was not found.'
      : 'The Winnow activation helper was not found. Build the backend companion or set WINNOW_ACTIVATION_HELPER_PATH to its prebuilt executable or DLL.',
  )
}

export function activationFrame(activation: ApplicationActivation): string {
  const value =
    activation.kind === 'game' ? { ...activation, ownershipId: String(activation.ownershipId) } : activation
  const frame = `${JSON.stringify({ version: 1, activation: value })}\n`
  if (Buffer.byteLength(frame) > 2048) throw new Error('The activation exceeds the protocol limit.')
  return frame
}

export function parseHelperActivation(value: unknown): ApplicationActivation {
  const action = validatedActivation(value)
  if (!action || !value || typeof value !== 'object') throw new Error('Invalid activation helper message.')
  const input = value as Record<string, unknown>
  const keys =
    action.kind === 'game'
      ? ['kind', 'ownershipId']
      : action.kind === 'plugin'
        ? ['kind', 'pluginId', 'releaseTag']
        : ['kind']
  if (
    Object.keys(input).length !== keys.length ||
    Object.keys(input).some((key) => !keys.includes(key)) ||
    (action.kind === 'game' &&
      (typeof input.ownershipId !== 'string' || String(action.ownershipId) !== input.ownershipId))
  )
    throw new Error('Invalid activation helper message.')
  return action
}

export interface ActivationHost {
  primary: boolean
  root?: string
  processId?: number
  dispose(): void
}
interface HostOptions extends ActivationHostLocation {
  dataDirectory?: string
  activation: ApplicationActivation
  signal: AbortSignal
  onActivation(activation: ApplicationActivation): void
  onLost(error: Error): void
  parentProcessId?: number
  launch?: typeof spawn
  timeoutMs?: number
}

/** The helper owns the protected Windows objects exactly while this parent's stdio connection lives. */
export async function startActivationHost(options: HostOptions): Promise<ActivationHost> {
  options.signal.throwIfAborted()
  const request = activationFrame(options.activation)
  const host = await activationHostCommand(options)
  options.signal.throwIfAborted()
  const parentProcessId = options.parentProcessId ?? process.pid
  const args = [...host.prefix, '--frontend-activation-helper', '--parent-pid', String(parentProcessId)]
  if (options.dataDirectory) args.push('--data-dir', options.dataDirectory)
  const child = (options.launch ?? spawn)(host.command, args, {
    cwd: host.cwd,
    windowsHide: true,
    stdio: 'pipe',
  }) as ChildProcessWithoutNullStreams
  let expected = false,
    ready = false,
    settled = false,
    diagnostic = '',
    buffer = Buffer.alloc(0)
  let resolveReady!: (value: ActivationHost) => void, rejectReady!: (error: unknown) => void
  const pending = new Promise<ActivationHost>((resolve, reject) => {
    resolveReady = resolve
    rejectReady = reject
  })
  const dispose = () => {
    if (expected) return
    expected = true
    clearTimeout(timer)
    options.signal.removeEventListener('abort', aborted)
    child.stdin.end()
  }
  const fail = (error: unknown) => {
    if (expected) return
    const failure =
      error instanceof Error ? error : new Error('The secure activation helper stopped unexpectedly.')
    if (!settled) {
      settled = true
      rejectReady(failure)
    } else if (ready) options.onLost(failure)
    dispose()
    if (!ready) child.kill()
  }
  const aborted = () => fail(options.signal.reason)
  const timer = setTimeout(
    () => fail(new Error('The secure activation helper did not respond within 45 seconds.')),
    options.timeoutMs ?? 45_000,
  )
  options.signal.addEventListener('abort', aborted, { once: true })
  child.stderr.on('data', (chunk: Buffer) => {
    diagnostic = (diagnostic + chunk.toString('utf8')).slice(-32768)
  })
  child.on('error', fail)
  child.stdin.on('error', fail)
  child.on('close', (code) =>
    fail(
      new BackendStartupFailure(
        code === 2 ? 2 : 3,
        scrubStartupDiagnostic(diagnostic) ||
          'The secure activation helper stopped unexpectedly. Winnow has closed to preserve single-instance ownership.',
      ),
    ),
  )
  child.stdout.on('data', (chunk: Buffer) => {
    if (expected) return
    buffer = Buffer.concat([buffer, chunk])
    try {
      while (true) {
        const end = buffer.indexOf(10)
        if ((end < 0 && buffer.length > 128 * 1024) || end > 128 * 1024)
          throw new Error('The activation helper response exceeds the protocol limit.')
        if (end < 0) break
        const frame = JSON.parse(buffer.subarray(0, end).toString('utf8')) as Record<string, unknown>
        buffer = buffer.subarray(end + 1)
        if (!ready && !settled) {
          if (frame.kind === 'forwarded' && frame.accepted === true && Object.keys(frame).length === 2) {
            settled = true
            resolveReady({ primary: false, dispose })
            dispose()
            return
          }
          if (
            Object.keys(frame).length !== 6 ||
            frame.kind !== 'primary' ||
            frame.processId !== child.pid ||
            frame.parentProcessId !== parentProcessId ||
            typeof frame.root !== 'string' ||
            !isAbsolute(frame.root) ||
            typeof frame.mutexName !== 'string' ||
            !/^Local\\Winnow\.Electron\.[A-F0-9]{64}$/.test(frame.mutexName) ||
            frame.pipeName !== `${frame.mutexName.slice('Local\\'.length)}.Activate` ||
            (options.dataDirectory &&
              resolve(frame.root)
                .replace(/[\\/]+$/, '')
                .toLowerCase() !==
                resolve(options.dataDirectory)
                  .replace(/[\\/]+$/, '')
                  .toLowerCase())
          )
            throw new Error('The activation helper did not establish secure ownership.')
          ready = true
          settled = true
          clearTimeout(timer)
          options.signal.removeEventListener('abort', aborted)
          resolveReady({ primary: true, root: frame.root, processId: child.pid, dispose })
        } else if (ready && frame.kind === 'activation' && Object.keys(frame).length === 2)
          options.onActivation(parseHelperActivation(frame.activation))
        else throw new Error('Unexpected activation helper response.')
      }
    } catch (error) {
      fail(error)
    }
  })
  child.stdout.on('end', () => {
    if (ready) fail(new Error('The secure activation helper connection closed unexpectedly.'))
  })
  try {
    child.stdin.write(request)
  } catch (error) {
    fail(error)
  }
  if (options.signal.aborted) aborted()
  return pending
}

/** Uses the same migration/fallback policy as the backend before selecting the frontend profile. */
export async function resolveBackendDataDirectory(
  options: ActivationHostLocation & { signal: AbortSignal },
): Promise<string> {
  options.signal.throwIfAborted()
  const host = await activationHostCommand(options)
  options.signal.throwIfAborted()
  const child = spawn(host.command, [...host.prefix, '--resolve-data-location'], {
    cwd: host.cwd,
    windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  return new Promise((resolve, reject) => {
    let stdout = '',
      stderr = '',
      settled = false
    const finish = (error?: unknown, root?: string) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      options.signal.removeEventListener('abort', abort)
      if (error) {
        child.kill()
        reject(error)
      } else resolve(root!)
    }
    const abort = () => finish(options.signal.reason)
    const timer = setTimeout(
      () => finish(new Error('The backend data-directory resolver timed out.')),
      45_000,
    )
    options.signal.addEventListener('abort', abort, { once: true })
    child.stdout.on('data', (chunk: Buffer) => {
      stdout += chunk.toString('utf8')
      if (Buffer.byteLength(stdout) > 128 * 1024)
        finish(new Error('The data-directory response exceeds the protocol limit.'))
    })
    child.stderr.on('data', (chunk: Buffer) => {
      stderr = (stderr + chunk.toString('utf8')).slice(-32768)
    })
    child.once('error', finish)
    child.once('close', (code) => {
      if (code !== 0)
        return finish(new BackendStartupFailure(code === 2 ? 2 : 3, scrubStartupDiagnostic(stderr)))
      try {
        const result = JSON.parse(stdout)
        if (typeof result.root !== 'string' || !isAbsolute(result.root))
          throw new Error('Invalid backend data-directory response.')
        finish(undefined, result.root)
      } catch (error) {
        finish(error)
      }
    })
    if (options.signal.aborted) abort()
  })
}

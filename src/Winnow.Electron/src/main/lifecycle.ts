import { spawn } from 'node:child_process'
import { access, mkdir } from 'node:fs/promises'
import { homedir } from 'node:os'
import { dirname, isAbsolute, join, resolve } from 'node:path'
import { readDiscovery, type Discovery } from './transport'
import { scrubStartupDiagnostic } from './startup-failure'
import { bundledBackendPaths } from './backend-location'

export function dataDirectoryArgument(args: string[]): string | undefined {
  const index = args.findIndex((arg) => arg === '--data-dir' || arg.startsWith('--data-dir='))
  if (index < 0) return undefined
  const argument = args[index]
  const value = argument.startsWith('--data-dir=') ? argument.slice(11) : args[index + 1]
  if (!value?.trim() || value.startsWith('-')) throw new Error('--data-dir requires a directory path')
  return resolve(value)
}
export function defaultDataRoot(): string {
  if (process.platform === 'win32') return process.env.LOCALAPPDATA ?? join(homedir(), 'AppData', 'Local')
  return process.env.XDG_DATA_HOME ?? join(homedir(), '.local', 'share')
}
export async function discoverBackend(explicitDirectory?: string, signal?: AbortSignal): Promise<Discovery> {
  signal?.throwIfAborted()
  if (explicitDirectory) return readDiscovery(explicitDirectory)
  // The backend decides migration and can continue at the legacy location if migration fails.
  const root = defaultDataRoot()
  let current: Discovery | undefined
  try {
    current = await readDiscovery(join(root, 'Winnow'))
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error
  }
  if (current && (await backendResponds(async () => current!, signal))) return current
  let legacy: Discovery | undefined
  try {
    legacy = await readDiscovery(join(root, 'Hoard'))
  } catch (error) {
    if (!current || (error as NodeJS.ErrnoException).code !== 'ENOENT') throw error
  }
  if (legacy && (await backendResponds(async () => legacy!, signal))) return legacy
  if (current) return current
  if (legacy) return legacy
  throw new Error('The backend has not published its connection yet')
}
export async function backendResponds(
  discover: () => Promise<Discovery>,
  signal?: AbortSignal,
): Promise<boolean> {
  try {
    const connection = await discover()
    const response = await fetch(new URL('/api/v1/health', connection.address), {
      headers: { Authorization: `Bearer ${connection.token}` },
      redirect: 'error',
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(1500)]) : AbortSignal.timeout(1500),
    })
    if (!response.ok) return false
    const health = (await response.json()) as { apiVersion?: string; epoch?: string }
    return health.apiVersion === '1' && health.epoch === connection.epoch
  } catch {
    return false
  }
}
export async function backendProcessIsRunning(discover: () => Promise<Discovery>): Promise<boolean> {
  try {
    const connection = await discover()
    process.kill(connection.processId, 0)
    return true
  } catch {
    return false
  }
}
export interface StartedBackend {
  processId: number
  exited: Promise<number | null>
  diagnostic(): string
}
export async function startBackend(options: {
  appPath: string
  resourcesPath: string
  packaged: boolean
  dataDirectory?: string
  args: string[]
}): Promise<StartedBackend> {
  if (!options.packaged && !options.dataDirectory)
    throw new Error('Development runs require --data-dir <throwaway directory>')
  if (options.dataDirectory) await mkdir(options.dataDirectory, { recursive: true })
  const configured = process.env.WINNOW_BACKEND_PATH
  if (configured && !isAbsolute(configured))
    throw new Error('WINNOW_BACKEND_PATH must be an absolute executable or DLL path')
  const candidates = configured ? [configured] : bundledBackendPaths(options.resourcesPath)
  let executable = candidates[0]
  if (!configured) {
    for (const candidate of candidates) {
      try {
        await access(candidate)
        executable = candidate
        break
      } catch {}
    }
  }
  const args = options.dataDirectory ? ['--data-dir', options.dataDirectory] : []
  if (!options.packaged || options.args.includes('--no-sync')) args.push('--no-sync')
  if (options.args.includes('--seed-sample')) args.push('--seed-sample')
  let command = executable
  let cwd = dirname(executable)
  try {
    await access(executable)
    if (executable.endsWith('.dll')) {
      command = 'dotnet'
      args.unshift(executable)
    }
  } catch {
    if (configured || options.packaged)
      throw new Error(
        'Backend companion was not found. Set WINNOW_BACKEND_PATH to a Winnow.Backend executable or start the backend separately.',
      )
    const project = resolve(options.appPath, '..', 'Winnow.Backend', 'Winnow.Backend.csproj')
    await access(project)
    command = 'dotnet'
    cwd = resolve(options.appPath, '..', '..')
    args.unshift('run', '--project', project, '--')
  }
  const child = spawn(command, args, {
    cwd,
    detached: true,
    windowsHide: true,
    stdio: ['ignore', 'ignore', 'pipe'],
  })
  let diagnostic = ''
  child.stderr?.setEncoding('utf8')
  child.stderr?.on('data', (chunk: string) => {
    diagnostic = (diagnostic + chunk).slice(-32768)
  })
  // The detached companion may outlive this frontend; its diagnostic pipe must not keep Electron alive.
  ;(child.stderr as (NodeJS.ReadableStream & { unref?(): void }) | null)?.unref?.()
  const exited = new Promise<number | null>((resolve) => child.once('close', (code) => resolve(code)))
  await new Promise<void>((resolve, reject) => {
    child.once('spawn', resolve)
    child.once('error', reject)
  })
  child.unref()
  return { processId: child.pid!, exited, diagnostic: () => scrubStartupDiagnostic(diagnostic) }
}

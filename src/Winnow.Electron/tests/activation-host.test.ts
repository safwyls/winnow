import { EventEmitter } from 'node:events'
import { PassThrough } from 'node:stream'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import {
  activationFrame,
  activationHostCommand,
  parseHelperActivation,
  startActivationHost,
} from '../src/main/activation-host'

const root = resolve('.tmp/activation-host-contract')
const executable = resolve('backend/Winnow.Backend.exe')
const location = {
  appPath: resolve('src/Winnow.Electron'),
  resourcesPath: resolve('resources'),
  platform: 'win32',
  environment: { WINNOW_ACTIVATION_HELPER_PATH: executable },
  exists: async () => {},
}
function child() {
  const value = Object.assign(new EventEmitter(), {
    pid: 912,
    kill: vi.fn(),
    stdin: new PassThrough(),
    stdout: new PassThrough(),
    stderr: new PassThrough(),
  })
  return value
}
function primary(rootValue = root) {
  return {
    kind: 'primary',
    root: rootValue,
    processId: 912,
    parentProcessId: 411,
    mutexName: `Local\\Winnow.Electron.${'A'.repeat(64)}`,
    pipeName: `Winnow.Electron.${'A'.repeat(64)}.Activate`,
  }
}
async function begin() {
  const process = child(),
    onLost = vi.fn(),
    onActivation = vi.fn(),
    abort = new AbortController()
  const launch = vi.fn(() => process)
  const pending = startActivationHost({
    ...location,
    dataDirectory: root,
    activation: { kind: 'show' },
    parentProcessId: 411,
    signal: abort.signal,
    launch: launch as never,
    onLost,
    onActivation,
  })
  await vi.waitFor(() => expect(launch).toHaveBeenCalledTimes(1))
  return { process, onLost, onActivation, abort, pending, launch }
}
describe('secure frontend activation host', () => {
  it('uses an independent helper fallback when the configured backend is missing and never dotnet run', async () => {
    const seen: string[] = []
    const result = await activationHostCommand({
      ...location,
      environment: { WINNOW_BACKEND_PATH: resolve('missing.exe') },
      exists: async (path) => {
        seen.push(path)
        if (!path.includes('resources')) throw Error('missing')
      },
    })
    expect(seen[0]).toContain('missing.exe')
    expect(result.command).toContain('resources')
    expect(result.prefix).toEqual([])
  })
  it('refuses an explicit invalid helper instead of falling back and launches DLLs as direct children', async () => {
    const exists = vi.fn(async () => {
      throw Error('missing')
    })
    await expect(activationHostCommand({ ...location, exists })).rejects.toThrow('explicitly configured')
    expect(exists).toHaveBeenCalledTimes(1)
    const dll = resolve('helper.dll')
    expect(
      await activationHostCommand({ ...location, environment: { WINNOW_ACTIVATION_HELPER_PATH: dll } }),
    ).toEqual({ command: 'dotnet', prefix: [dll], cwd: resolve('.') })
  })
  it('keeps positive Int64 IDs exact in both typed directions and rejects malformed messages', () => {
    const max = '9223372036854775807'
    expect(JSON.parse(activationFrame({ kind: 'game', ownershipId: max })).activation.ownershipId).toBe(max)
    expect(parseHelperActivation({ kind: 'game', ownershipId: max })).toEqual({
      kind: 'game',
      ownershipId: max,
    })
    for (const bad of [42, '042', '0', '9223372036854775808'])
      expect(() => parseHelperActivation({ kind: 'game', ownershipId: bad })).toThrow()
    expect(() => parseHelperActivation({ kind: 'show', extra: true })).toThrow()
    expect(() =>
      parseHelperActivation({ kind: 'plugin', pluginId: 'xbox&release=v1.0.0', releaseTag: 'v2.0.0' }),
    ).toThrow()
  })
  it('accepts the protected helper identity, forwards ordered actions and closes its lease on disposal', async () => {
    const host = await begin()
    host.process.stdout.write(JSON.stringify(primary()) + '\n')
    const owner = await host.pending
    expect(owner.primary).toBe(true)
    for (const kind of ['fullscreen', 'show'])
      host.process.stdout.write(JSON.stringify({ kind: 'activation', activation: { kind } }) + '\n')
    expect(host.onActivation.mock.calls.map(([value]) => value.kind)).toEqual(['fullscreen', 'show'])
    owner.dispose()
    expect(host.process.stdin.writableEnded).toBe(true)
    host.process.emit('close', 0)
    expect(host.onLost).not.toHaveBeenCalled()
  })
  it('keeps established ownership through a canceled startup or held quit drain until explicit disposal', async () => {
    const host = await begin()
    host.process.stdout.write(JSON.stringify(primary()) + '\n')
    const owner = await host.pending
    host.abort.abort()
    expect(host.process.stdin.writableEnded).toBe(false)
    expect(host.onLost).not.toHaveBeenCalled()
    owner.dispose()
    expect(host.process.stdin.writableEnded).toBe(true)
  })
  it('forwards a secondary without constructing a primary and does not treat its exit as failure', async () => {
    const host = await begin()
    host.process.stdout.write('{"kind":"forwarded","accepted":true}\n')
    expect((await host.pending).primary).toBe(false)
    host.process.emit('close', 0)
    expect(host.onLost).not.toHaveBeenCalled()
  })
  it('fails visibly on helper loss after ownership and rejects a mismatched root before ownership', async () => {
    const owned = await begin()
    owned.process.stdout.write(JSON.stringify(primary()) + '\n')
    await owned.pending
    owned.process.emit('close', 9)
    expect(owned.onLost).toHaveBeenCalledTimes(1)
    const wrong = await begin()
    const rejected = expect(wrong.pending).rejects.toThrow('secure ownership')
    wrong.process.stdout.write(JSON.stringify(primary(resolve('other'))) + '\n')
    await rejected
    expect(wrong.process.stdin.writableEnded).toBe(true)
  })
  it('retains exit2 and bounded diagnostics while rejecting oversized frames and cancelling the handshake', async () => {
    const refusal = await begin()
    const rejected = expect(refusal.pending).rejects.toMatchObject({ exitCode: 2 })
    refusal.process.stderr.write('The data directory cannot be used.')
    refusal.process.emit('close', 2)
    await rejected
    const oversized = await begin()
    const bounded = expect(oversized.pending).rejects.toThrow('protocol limit')
    oversized.process.stdout.write('x'.repeat(128 * 1024 + 1))
    await bounded
    expect(oversized.process.kill).toHaveBeenCalledTimes(1)
    const cancelled = await begin()
    const aborted = expect(cancelled.pending).rejects.toMatchObject({ name: 'AbortError' })
    cancelled.abort.abort()
    await aborted
    expect(cancelled.process.stdin.writableEnded).toBe(true)
  })
})

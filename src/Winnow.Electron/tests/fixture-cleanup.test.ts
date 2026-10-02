import { createServer } from 'node:http'
import { mkdir, mkdtemp, rmdir, unlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import { spawn } from 'node:child_process'
import { once } from 'node:events'
import type { ElectronApplication } from '@playwright/test'
import { expect, it, vi } from 'vitest'
import { closeFixture, trackFixture } from './electron/fixture-cleanup'

it.each([0, 3])(
  'retains the actual owned child after Playwright disposes its process handle (exit %s)',
  async (code) => {
    const child = spawn(process.execPath, ['-e', `process.exit(${code})`], { windowsHide: true })
    const handle = vi.fn(() => child)
    const application = {
      process: handle,
      evaluate: vi.fn(),
      close: vi.fn(),
    } as unknown as ElectronApplication
    trackFixture(application)
    const [exitCode] = await once(child, 'exit')
    expect(exitCode).toBe(code)
    handle.mockImplementation(() => {
      throw new TypeError("Cannot read properties of undefined (reading '_object')")
    })
    if (code === 0) await expect(closeFixture(application)).resolves.toBeUndefined()
    else await expect(closeFixture(application)).rejects.toThrow('Electron fixture exited 3')
    expect(application.evaluate).not.toHaveBeenCalled()
    expect(application.close).not.toHaveBeenCalled()
    expect(handle).toHaveBeenCalledTimes(1)
  },
)

it.each([200, 503])(
  'cleans up a backend published after an early fixture failure and preserves shutdown status %s',
  async (status) => {
    const requests: { method?: string; path?: string; authorization?: string }[] = []
    const server = createServer((request, response) => {
      requests.push({
        method: request.method,
        path: request.url,
        authorization: request.headers.authorization,
      })
      response.writeHead(status).end()
    })
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
    const address = server.address() as { port: number }
    const directory = await mkdtemp(join(tmpdir(), 'winnow-cleanup-'))
    const backend = join(directory, 'backend'),
      endpoint = join(backend, 'endpoint.json')
    await mkdir(backend)
    let probes = 0
    const processProbe = vi.spyOn(process, 'kill').mockImplementation((pid, signal) => {
      expect(pid).toBe(471381)
      expect(signal).toBe(0)
      if (++probes <= 2) return true
      throw Object.assign(new Error('Fixture process exited'), { code: 'ESRCH' })
    })
    try {
      const shutdown = closeFixture(undefined, directory)
      const result =
        status === 200
          ? expect(shutdown).resolves.toBeUndefined()
          : expect(shutdown).rejects.toThrow('Fixture backend shutdown returned 503')
      await delay(30)
      expect(requests).toEqual([])
      await writeFile(
        endpoint,
        JSON.stringify({
          address: `http://127.0.0.1:${address.port}/`,
          token: 'fixture-token',
          processId: 471381,
        }),
      )
      await result
      expect(probes).toBe(status === 200 ? 3 : 0)
      expect(requests).toEqual([
        { method: 'POST', path: '/api/v1/lifecycle/shutdown', authorization: 'Bearer fixture-token' },
      ])
    } finally {
      processProbe.mockRestore()
      await new Promise<void>((resolve) => server.close(() => resolve()))
      await unlink(endpoint)
      await rmdir(backend)
      await rmdir(directory)
    }
  },
)

it('refuses a discovery address outside the fixture loopback root', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'winnow-cleanup-'))
  const backend = join(directory, 'backend'),
    endpoint = join(backend, 'endpoint.json')
  await mkdir(backend)
  try {
    await writeFile(endpoint, JSON.stringify({ address: 'https://example.invalid/', token: 'fixture-token' }))
    await expect(closeFixture(undefined, directory)).rejects.toThrow('Unexpected fixture backend address')
  } finally {
    await unlink(endpoint)
    await rmdir(backend)
    await rmdir(directory)
  }
})

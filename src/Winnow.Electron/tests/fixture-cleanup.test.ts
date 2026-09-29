import { createServer } from 'node:http'
import { mkdir, mkdtemp, rmdir, unlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import { expect, it } from 'vitest'
import { closeFixture } from './electron/fixture-cleanup'

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
        JSON.stringify({ address: `http://127.0.0.1:${address.port}/`, token: 'fixture-token' }),
      )
      await result
      expect(requests).toEqual([
        { method: 'POST', path: '/api/v1/lifecycle/shutdown', authorization: 'Bearer fixture-token' },
      ])
    } finally {
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

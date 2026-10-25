import { afterEach, describe, expect, it, vi } from 'vitest'
import { createHash } from 'node:crypto'
import { createServer, request, type Server, type RequestOptions } from 'node:http'
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import {
  distributionRelease,
  downloadDistribution,
  type DistributionRelease,
} from '../src/main/distribution-release'
import { UpdateHttpExecutor } from '../src/main/update-http'

const repository = 'https://github.com/safwyls/winnow'
const payload = Buffer.from('isolated primary distribution bytes; never executed')
const digest = createHash('sha256').update(payload).digest('hex')
const options = { version: '1.0.0', includeBeta: false, runtime: 'win-x64', kind: 'installed' } as const
function release(version = '2.0.0', name = `Winnow-${version}-win-x64-setup.exe`) {
  return {
    tag_name: `v${version}`,
    draft: false,
    prerelease: version.includes('-'),
    assets: [
      {
        name,
        browser_download_url: `${repository}/releases/download/v${version}/${name}`,
        size: payload.length,
        digest: `sha256:${digest}`,
      },
    ],
  }
}
function pages(...values: unknown[]) {
  return vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(values.shift()), { status: 200 })),
  )
}
const resources: { directory: string; server: Server }[] = []
afterEach(async () => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.useRealTimers()
  for (const { server, directory } of resources.splice(0)) {
    server.closeAllConnections()
    await new Promise<void>((done) => server.close(() => done()))
    await rm(directory, { recursive: true, force: true })
  }
})

describe('primary distribution release metadata', () => {
  it.each([
    ['win-x64', 'installed', 'Winnow-2.0.0-win-x64-setup.exe'],
    ['win-x64', 'portable', 'Winnow-2.0.0-win-x64.zip'],
    ['linux-x64', 'portable', 'Winnow-2.0.0-linux-x64.tar.gz'],
    ['linux-x64', 'managed', 'Winnow-2.0.0-linux-x64.deb'],
  ] as const)('selects the exact official %s %s primary artifact', async (runtime, kind, name) => {
    pages([release('2.0.0', name)])
    expect(await distributionRelease({ ...options, runtime, kind })).toEqual({
      version: '2.0.0',
      releaseUrl: `${repository}/releases/tag/v2.0.0`,
      assetUrl: `${repository}/releases/download/v2.0.0/${name}`,
      downloadUrl: `${repository}/releases/download/v2.0.0/${name}`,
      size: payload.length,
      sha256: digest,
    })
    expect(fetch).toHaveBeenCalledWith(
      'https://api.github.com/repos/safwyls/winnow/releases?per_page=100&page=1',
      expect.objectContaining({ redirect: 'error', signal: expect.any(AbortSignal) }),
    )
  })
  it('discovers later pages and orders semantic versions rather than GitHub array position', async () => {
    pages(
      [release('2.9.0'), ...Array.from({ length: 99 }, () => release('1.0.0'))],
      [release('2.10.0'), release('2.2.0')],
    )
    expect((await distributionRelease(options))?.version).toBe('2.10.0')
    expect(fetch).toHaveBeenCalledTimes(2)
  })
  it.each([false, true])(
    'preserves beta, draft, development and CI policy with beta=%s',
    async (includeBeta) => {
      pages([
        release('2.0.0'),
        release('3.0.0-beta.10'),
        release('3.0.0-beta.2'),
        release('9.0.0-dev.1'),
        release('9.0.0-ci.1'),
        { ...release('10.0.0'), draft: true },
      ])
      expect((await distributionRelease({ ...options, includeBeta }))?.version).toBe(
        includeBeta ? '3.0.0-beta.10' : '2.0.0',
      )
    },
  )
  it.each(['1.0.0-dev.1', '1.0.0-ci.7', 'not-a-version'])(
    'does not check nonrelease running version %s',
    async (version) => {
      pages([])
      expect(await distributionRelease({ ...options, version })).toBeNull()
      expect(fetch).not.toHaveBeenCalled()
    },
  )
  it('returns null for no newer release, including older prereleases and same-version assets', async () => {
    pages([release('1.0.0'), release('1.0.0-beta.2')])
    expect(await distributionRelease({ ...options, includeBeta: true })).toBeNull()
  })
  it.each([
    { browser_download_url: 'https://attacker.invalid/update.exe' },
    { browser_download_url: `${repository}/releases/download/v2.0.0/Winnow-2.0.0-win-x64-setup.exe?x=1` },
    { size: 0 },
    { size: -1 },
    { size: 1.5 },
    { size: 4 * 1024 ** 3 + 1 },
    { digest: null },
    { digest: `sha512:${digest}` },
    { digest: 'sha256:missing' },
  ])('refuses invalid newest artifact without falling back: %j', async (invalid) => {
    const latest = release()
    Object.assign(latest.assets[0], invalid)
    pages([release('1.5.0'), latest])
    await expect(distributionRelease(options)).rejects.toThrow('invalid URL, size or SHA-256')
  })
  it.each(['missing', 'duplicate', 'electron'] as const)(
    'does not substitute an older release for %s primary asset',
    async (mode) => {
      const latest =
        mode === 'electron' ? release('2.0.0', 'Winnow-Electron-2.0.0-win-x64-Setup.exe') : release()
      if (mode === 'missing') latest.assets = []
      if (mode === 'duplicate') latest.assets.push({ ...latest.assets[0] })
      pages([latest, release('1.5.0')])
      await expect(distributionRelease(options)).rejects.toThrow('no unique distribution')
    },
  )
  it('bounds all metadata pages to4MiB while streaming, and cancels unread bytes', async () => {
    const cancel = vi.fn()
    let reads = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(
            new ReadableStream({
              pull(controller) {
                reads++
                controller.enqueue(new Uint8Array(1024 ** 2))
              },
              cancel,
            }),
          ),
      ),
    )
    await expect(distributionRelease(options)).rejects.toThrow('too large')
    expect(cancel).toHaveBeenCalledOnce()
    expect(reads).toBeLessThanOrEqual(6)
  })
  it('cancels a stalled response body at30seconds rather than only timing out the response headers', async () => {
    vi.useFakeTimers()
    const abort = new AbortController()
    vi.spyOn(AbortSignal, 'timeout').mockImplementation((milliseconds) => {
      setTimeout(() => abort.abort(), milliseconds)
      return abort.signal
    })
    const cancel = vi.fn()
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(new ReadableStream({ cancel }))),
    )
    const result = distributionRelease(options)
    const rejected = expect(result).rejects.toThrow()
    await vi.advanceTimersByTimeAsync(30_000)
    await rejected
    expect(cancel).toHaveBeenCalledOnce()
  })
  it('honors caller cancellation before requests and while the body is pending', async () => {
    const controller = new AbortController(),
      cancel = vi.fn()
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(new ReadableStream({ cancel }))),
    )
    const result = distributionRelease({ ...options, signal: controller.signal })
    await Promise.resolve()
    controller.abort()
    await expect(result).rejects.toThrow()
    expect(cancel).toHaveBeenCalledOnce()
    vi.mocked(fetch).mockClear()
    await expect(distributionRelease({ ...options, signal: controller.signal })).rejects.toThrow()
    expect(fetch).not.toHaveBeenCalled()
  })
  it('rejects incomplete pagination, invalid shape and HTTP errors', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify(Array.from({ length: 100 }, () => release('1.0.0'))))),
    )
    await expect(distributionRelease(options)).rejects.toThrow('completely')
    expect(fetch).toHaveBeenCalledTimes(10)
    pages({ unexpected: true })
    await expect(distributionRelease(options)).rejects.toThrow('Invalid release')
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('', { status: 503 })),
    )
    await expect(distributionRelease(options)).rejects.toThrow('could not be read')
  })
})

async function downloadFixture() {
  const directory = await mkdtemp(join(tmpdir(), 'winnow-primary-download-'))
  let mode: 'valid' | 'bad-hash' | 'short' | 'large' | 'paused' | 'foreign' | 'github' | 'loop' = 'valid'
  let entered!: () => void
  const ready = new Promise<void>((resolve) => {
    entered = resolve
  })
  const urls: string[] = []
  const server = createServer((req, res) => {
    if (mode === 'foreign' || mode === 'loop' || (mode === 'github' && req.url !== '/approved')) {
      res.writeHead(302, {
        location:
          mode === 'foreign'
            ? 'https://attacker.invalid/a'
            : mode === 'loop'
              ? 'https://github.com/loop'
              : 'https://release-assets.githubusercontent.com/approved',
      })
      res.end()
      return
    }
    res.writeHead(200)
    if (mode === 'paused') {
      res.write(payload.subarray(0, 5))
      entered()
      return
    }
    res.end(
      mode === 'bad-hash'
        ? Buffer.alloc(payload.length)
        : mode === 'short'
          ? payload.subarray(0, 5)
          : mode === 'large'
            ? Buffer.concat([payload, payload])
            : payload,
    )
  })
  resources.push({ directory, server })
  await new Promise<void>((done) => server.listen(0, '127.0.0.1', done))
  const address = server.address()
  if (!address || typeof address === 'string') throw Error('No fixture port')
  // Only substitute socket destination; real executor redirect policy, streaming and filesystem remain active.
  vi.spyOn(UpdateHttpExecutor.prototype, 'createRequest').mockImplementation(
    (options: RequestOptions, callback: (response: any) => void) => {
      urls.push(`${options.protocol}//${options.hostname}${options.path}`)
      return request(
        { hostname: '127.0.0.1', port: address.port, path: options.path, method: options.method },
        callback,
      ) as unknown as Electron.ClientRequest
    },
  )
  const name = release().assets[0].browser_download_url
  const selected: DistributionRelease = {
    version: '2.0.0',
    releaseUrl: `${repository}/releases/tag/v2.0.0`,
    downloadUrl: name,
    assetUrl: name,
    size: payload.length,
    sha256: digest,
  }
  return {
    directory,
    selected,
    destination: join(directory, 'staged.zip'),
    urls,
    ready,
    setMode(value: typeof mode) {
      mode = value
    },
  }
}

describe('verified primary distribution stream', () => {
  it('follows approved GitHub redirects and stages only exact verified bytes with final100%', async () => {
    const f = await downloadFixture(),
      progress: number[] = []
    f.setMode('github')
    await downloadDistribution(f.selected, f.destination, new AbortController().signal, (value) =>
      progress.push(value),
    )
    expect(await readFile(f.destination)).toEqual(payload)
    expect(await readdir(f.directory)).toEqual(['staged.zip'])
    expect(f.urls).toHaveLength(2)
    expect(f.urls[1]).toBe('https://release-assets.githubusercontent.com/approved')
    expect(progress.at(-1)).toBe(100)
    expect(progress.slice(0, -1).every((value) => value < 100)).toBe(true)
  })
  it.each(['bad-hash', 'short', 'large', 'foreign', 'loop'] as const)(
    'cleans failed %s stream and permits retry without altering existing destination',
    async (mode) => {
      const f = await downloadFixture()
      await writeFile(f.destination, 'previous staged content')
      f.setMode(mode)
      await expect(
        downloadDistribution(f.selected, f.destination, new AbortController().signal, () => {}),
      ).rejects.toThrow()
      expect(await readdir(f.directory)).toEqual(['staged.zip'])
      expect(await readFile(f.destination, 'utf8')).toBe('previous staged content')
      if (mode === 'foreign') expect(f.urls).toHaveLength(1)
      if (mode === 'loop') expect(f.urls.length).toBeLessThanOrEqual(11)
      f.setMode('valid')
      await downloadDistribution(f.selected, f.destination, new AbortController().signal, () => {})
      expect(await readFile(f.destination)).toEqual(payload)
    },
  )
  it('aborts an active chunked stream, closes/removes the partial and retries successfully', async () => {
    const f = await downloadFixture(),
      controller = new AbortController()
    f.setMode('paused')
    const downloading = downloadDistribution(f.selected, f.destination, controller.signal, () => {})
    const rejected = expect(downloading).rejects.toThrow()
    await f.ready
    controller.abort()
    await rejected
    expect(await readdir(f.directory)).toEqual([])
    f.setMode('valid')
    await downloadDistribution(f.selected, f.destination, new AbortController().signal, () => {})
    expect(await readFile(f.destination)).toEqual(payload)
  })
  it('refuses a changed URL or invalid metadata before opening any socket', async () => {
    const f = await downloadFixture()
    for (const invalid of [{ assetUrl: 'https://attacker.invalid/a' }, { size: 0 }, { sha256: '' }])
      await expect(
        downloadDistribution(
          { ...f.selected, ...invalid },
          f.destination,
          new AbortController().signal,
          () => {},
        ),
      ).rejects.toThrow('Invalid distribution')
    expect(f.urls).toEqual([])
    expect(await readdir(f.directory)).toEqual([])
  })
})

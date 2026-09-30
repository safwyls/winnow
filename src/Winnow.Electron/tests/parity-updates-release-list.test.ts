import { afterEach, describe, expect, it, vi } from 'vitest'
import { manualRelease } from '../src/main/manual-update-release'
import {
  electronAssetName,
  newerRelease,
  updateRepository,
  validateUpdateFiles,
} from '../src/main/update-policy'

afterEach(() => vi.unstubAllGlobals())
function release(version: string, properties = {}) {
  return {
    tag_name: `v${version}`,
    draft: false,
    prerelease: false,
    assets: [{ name: electronAssetName(version, 'win32', 'x64') }],
    ...properties,
  }
}
function pages(...values: unknown[]) {
  const fetcher = vi.fn(async (_url: string, _init: RequestInit) => Response.json(values.shift() ?? []))
  vi.stubGlobal('fetch', fetcher)
  return fetcher
}
describe('release list and version parity', () => {
  it.each([
    ['1.0.0-beta.2', '1.0.0-beta.10'],
    ['1.0.0-beta.10', '1.0.0'],
    ['1.0.0', '1.0.1-beta.1'],
    ['1.0.0-1', '1.0.0-alpha'],
    ['1.0.0-alpha', '1.0.0-alpha.1'],
  ])('orders %s before %s in both directions', (earlier, later) => {
    expect(newerRelease(later, earlier, true)).toBe(true)
    expect(newerRelease(earlier, later, true)).toBe(false)
    expect(newerRelease(later, later, true)).toBe(false)
  })
  it.each([
    [false, '2.0.0'],
    [true, '5.0.0-beta.1'],
  ] as const)('filters drafts both prerelease forms and downgrades with beta %s', async (beta, expected) => {
    pages([
      release('0.9.0'),
      release('1.0.0'),
      release('2.0.0'),
      release('3.0.0', { draft: true }),
      release('4.0.0', { prerelease: true }),
      release('5.0.0-beta.1'),
    ])
    expect((await manualRelease('1.0.0', beta, 'win32', 'x64'))?.version).toBe(expected)
  })
  it.each([
    ['win32', 'x64', 'win-x64-Setup.exe'],
    ['win32', 'arm64', 'win-arm64-Setup.exe'],
    ['linux', 'x64', 'linux-x64.AppImage'],
    ['linux', 'arm64', 'linux-arm64.AppImage'],
    ['darwin', 'x64', 'mac-x64.dmg'],
    ['darwin', 'arm64', 'mac-arm64.dmg'],
  ])('selects exact %s %s manual assets and ignores foreign architecture', async (platform, arch, suffix) => {
    pages([
      release('9.0.0', { assets: [{ name: 'Winnow-Electron-9.0.0-other.zip' }] }),
      release('2.0.0', { assets: [{ name: `Winnow-Electron-2.0.0-${suffix}` }] }),
    ])
    expect((await manualRelease('1.0.0', false, platform, arch))?.version).toBe('2.0.0')
  })
  it.each(['win32', 'linux'])('validates exact %s native architecture metadata', (platform) => {
    for (const arch of ['x64', 'arm64', 'ia32']) {
      const info = {
        version: '2.0.0',
        files: [
          { url: electronAssetName('2.0.0', platform, arch), sha512: Buffer.alloc(64).toString('base64') },
        ],
      }
      expect(() => validateUpdateFiles(info, platform, arch)).not.toThrow()
      expect(() => validateUpdateFiles(info, platform, arch === 'x64' ? 'arm64' : 'x64')).toThrow(
        'architecture',
      )
    }
  })
  it('scans later pages and replaces foreign asset URLs with the canonical release page', async () => {
    const fetcher = pages(
      Array.from({ length: 100 }, () => release('1.0.0')),
      [
        release('2.0.0'),
        release('9.0.0', {
          assets: [
            {
              name: electronAssetName('9.0.0', 'win32', 'x64'),
              browser_download_url: 'https://attacker.invalid/payload.exe',
            },
          ],
        }),
      ],
    )
    expect(await manualRelease('1.0.0', false, 'win32', 'x64')).toEqual({
      version: '9.0.0',
      releaseUrl: `${updateRepository}/releases/tag/v9.0.0`,
      downloadUrl: `${updateRepository}/releases/tag/v9.0.0`,
    })
    expect(fetcher).toHaveBeenCalledTimes(2)
    expect(fetcher.mock.calls.map(([url]) => String(url))).toEqual(
      [1, 2].map((page) => `https://api.github.com/repos/safwyls/winnow/releases?per_page=100&page=${page}`),
    )
    expect(fetcher).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({ redirect: 'error' }))
  })
  it.each([403, 404, 429, 500])('refuses HTTP %s without reporting up to date', async (status) => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('', { status })),
    )
    await expect(manualRelease('1.0.0', false, 'win32', 'x64')).rejects.toThrow('could not be read')
  })
  it('bounds pagination and reports an incomplete check instead of an up to date claim', async () => {
    const fetcher = vi.fn(async () => Response.json(Array.from({ length: 100 }, () => release('1.0.0'))))
    vi.stubGlobal('fetch', fetcher)
    await expect(manualRelease('1.0.0', false, 'win32', 'x64')).rejects.toThrow('completely')
    expect(fetcher).toHaveBeenCalledTimes(10)
  })
  it('enforces one response budget across every page', async () => {
    pages(
      Array.from({ length: 100 }, () => ({ ...release('1.0.0'), body: 'x'.repeat(25_000) })),
      [{ ...release('2.0.0'), body: 'x'.repeat(2_000_000) }],
    )
    await expect(manualRelease('1.0.0', false, 'win32', 'x64')).rejects.toThrow('too large')
  })
})

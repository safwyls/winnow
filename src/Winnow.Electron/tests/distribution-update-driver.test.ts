import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { createHash } from 'node:crypto'
vi.mock('../src/main/distribution-release', () => ({
  distributionRelease: vi.fn(),
  downloadDistribution: vi.fn(),
}))
import { distributionUpdateDriver } from '../src/main/distribution-update-driver'
import type { DistributionRelease } from '../src/main/distribution-release'

const roots: string[] = []
afterEach(async () => {
  for (const root of roots.splice(0)) await rm(root, { recursive: true, force: true })
})
async function fixture(kind: 'portable' | 'installed' | 'managed' = 'portable') {
  const root = await mkdtemp(join(tmpdir(), 'winnow-distribution-'))
  roots.push(root)
  const bytes = Buffer.from('verified installer archive')
  const release: DistributionRelease = {
    version: '0.2.0',
    releaseUrl: 'https://github.com/safwyls/winnow/releases/tag/v0.2.0',
    downloadUrl: 'https://github.com/safwyls/winnow/releases/tag/v0.2.0',
    assetUrl: 'https://github.com/safwyls/winnow/releases/download/v0.2.0/Winnow-0.2.0-win-x64.zip',
    sha256: createHash('sha256').update(bytes).digest('hex'),
    size: bytes.length,
  }
  const journal = join(root, 'journal.json')
  const helper = vi.fn(async (_path: string, args: string[]) => (args[0] === 'stage' ? journal : ''))
  const download = vi.fn(
    async (
      _release: DistributionRelease,
      path: string,
      _signal: AbortSignal,
      progress: (value: number) => void,
    ) => {
      await writeFile(path, bytes)
      progress(100)
    },
  )
  const quit = vi.fn()
  const check = vi.fn(async () => release)
  const driver = distributionUpdateDriver({
    version: '0.1.0',
    runtime: 'win-x64',
    kind,
    supported: kind !== 'managed',
    installation: root,
    executable: 'Winnow.exe',
    dataDirectory: join(root, 'selected library'),
    args: ['--seed-sample', '--epic-login', '--unknown', '--no-sync'],
    quit,
    check,
    helper,
    download,
  })
  return { root, driver, helper, download, quit, release, journal, check }
}
describe('primary distribution update orchestration', () => {
  it('retains verified staging when the same channel finds the same artifact again', async () => {
    const f = await fixture()
    await f.driver.check(false)
    await f.driver.download(f.release, new AbortController().signal, vi.fn())
    await f.driver.check(false)
    await f.driver.install()
    expect(f.download).toHaveBeenCalledOnce()
    expect(f.quit).toHaveBeenCalledOnce()
  })
  it('revokes readiness when a staged version changes its digest', async () => {
    const f = await fixture()
    await f.driver.check(false)
    await f.driver.download(f.release, new AbortController().signal, vi.fn())
    f.check.mockResolvedValueOnce({ ...f.release, sha256: '0'.repeat(64) })
    await expect(f.driver.check(false)).rejects.toThrow('metadata changed')
    await expect(f.driver.install()).rejects.toThrow('Download')
    expect(f.quit).not.toHaveBeenCalled()
  })
  it.each(['portable', 'installed'] as const)(
    '%s stages verified bytes and only quits after confirmed handoff',
    async (kind) => {
      const f = await fixture(kind)
      await f.driver.check(false)
      await f.driver.download(f.release, new AbortController().signal, vi.fn())
      expect(f.quit).not.toHaveBeenCalled()
      await f.driver.install()
      expect(f.quit).toHaveBeenCalledOnce()
      const args = f.helper.mock.calls.at(-1)![1]
      expect(args[0]).toBe(kind === 'portable' ? 'handoff' : 'installed-handoff')
      expect(args).not.toContain('--seed-sample')
      expect(args).not.toContain('--epic-login')
      expect(args).not.toContain('--unknown')
      if (kind === 'installed') {
        expect(args).toContain('--no-sync')
        expect(args[args.indexOf('--data-dir') + 1]).toBe(join(f.root, 'selected library'))
      } else {
        const stage = f.helper.mock.calls[0][1]
        expect(stage).toContain('--no-sync')
        expect(stage[stage.indexOf('--data-dir') + 1]).toBe(join(f.root, 'selected library'))
        expect(stage[stage.indexOf('--transaction') + 1]).toMatch(/^[a-f0-9]{32}$/)
      }
      await f.driver.discard()
      expect(f.helper.mock.calls.some(([, args]) => args[0] === 'discard')).toBe(false)
    },
  )
  it('never stages or installs a package managed release', async () => {
    const f = await fixture('managed')
    expect(await f.driver.check(false)).toEqual(f.release)
    expect(f.driver.supported).toBe(false)
    await expect(f.driver.download(f.release, new AbortController().signal, vi.fn())).rejects.toThrow(
      'supported',
    )
    await expect(f.driver.install()).rejects.toThrow('Download')
    expect(f.helper).not.toHaveBeenCalled()
  })
  it('rejects stale release identity before any download', async () => {
    const f = await fixture()
    await f.driver.check(false)
    await expect(
      f.driver.download({ ...f.release, version: '9.0.0' }, new AbortController().signal, vi.fn()),
    ).rejects.toThrow('supported')
    expect(f.download).not.toHaveBeenCalled()
  })
  it('waits for staging then discards its own transaction on cancellation', async () => {
    const f = await fixture()
    const cancellation = new AbortController()
    f.helper.mockImplementation(async (_path, args) => {
      if (args[0] === 'stage') {
        cancellation.abort()
        return f.journal
      }
      return ''
    })
    await f.driver.check(false)
    await expect(f.driver.download(f.release, cancellation.signal, vi.fn())).rejects.toThrow()
    const stage = f.helper.mock.calls[0][1],
      discard = f.helper.mock.calls[1][1]
    expect(discard).toEqual([
      'discard',
      '--journal',
      f.journal,
      '--transaction',
      stage[stage.indexOf('--transaction') + 1],
    ])
    const downloaded = f.download.mock.calls[0][1]
    await expect(readFile(downloaded)).rejects.toThrow()
    expect(f.quit).not.toHaveBeenCalled()
  })
  it('rechecks staged bytes before handoff and removes corrupt files', async () => {
    const f = await fixture()
    await f.driver.check(false)
    await f.driver.download(f.release, new AbortController().signal, vi.fn())
    const downloaded = f.download.mock.calls[0][1]
    await writeFile(downloaded, 'changed')
    await expect(f.driver.install()).rejects.toThrow('checksum')
    expect(f.helper.mock.calls.some(([, args]) => args[0] === 'handoff')).toBe(false)
    await expect(readFile(downloaded)).rejects.toThrow()
    expect(f.quit).not.toHaveBeenCalled()
  })
  it('failed handoff leaves the app open and permits a verified retry', async () => {
    const f = await fixture()
    await f.driver.check(false)
    await f.driver.download(f.release, new AbortController().signal, vi.fn())
    f.helper.mockImplementation(async (_path, args) => {
      if (args[0] === 'handoff') throw Error('helper could not prepare')
      return args[0] === 'stage' ? f.journal : ''
    })
    await expect(f.driver.install()).rejects.toThrow('prepare')
    expect(f.quit).not.toHaveBeenCalled()
    expect(f.helper.mock.calls.at(-1)![1][0]).toBe('discard')
    await f.driver.download(f.release, new AbortController().signal, vi.fn())
    expect(f.download).toHaveBeenCalledTimes(2)
    await f.driver.discard()
  })
  it('channel checks revoke staging before selecting another release', async () => {
    const f = await fixture()
    await f.driver.check(true)
    await f.driver.download(f.release, new AbortController().signal, vi.fn())
    const path = f.download.mock.calls[0][1]
    await f.driver.check(false)
    await expect(readFile(path)).rejects.toThrow()
    await expect(f.driver.install()).rejects.toThrow('Download')
    expect(dirname(path)).toContain('electron-')
  })
})

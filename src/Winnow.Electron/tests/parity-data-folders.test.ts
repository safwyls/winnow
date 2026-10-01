import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { openDataFolder } from '../src/main/data-folders'

const directories: string[] = []
afterEach(async () => {
  for (const directory of directories.splice(0)) await rm(directory, { recursive: true, force: true })
})
async function fixture() {
  const root = await mkdtemp(join(tmpdir(), 'winnow-data-folder-'))
  directories.push(root)
  return {
    root,
    ports: {
      pluginDirectory: vi.fn(async () => join(root, 'plugins')),
      createDirectory: (path: string) => mkdir(path, { recursive: true }),
      openPath: vi.fn(async (_path: string) => ''),
    },
  }
}

describe('selected backend folders', () => {
  it('returns the exact resolved logs path on shell failure and allows a successful retry', async () => {
    const { root, ports } = await fixture()
    ports.openPath.mockResolvedValueOnce('untrusted OS detail')
    await expect(openDataFolder('logs', ports)).rejects.toThrow(
      `Couldn't open the logs folder. Open it manually: ${join(root, 'logs')}`,
    )
    await expect(openDataFolder('logs', ports)).resolves.toBeUndefined()
    expect(ports.openPath.mock.calls).toEqual([[join(root, 'logs')], [join(root, 'logs')]])
  })
  it('returns the same manual path when a real file blocks creating the logs directory', async () => {
    const { root, ports } = await fixture()
    const path = join(root, 'logs')
    await writeFile(path, 'A file blocks the directory')
    await expect(openDataFolder('logs', ports)).rejects.toThrow(
      `Couldn't open the logs folder. Open it manually: ${path}`,
    )
    expect(ports.openPath).not.toHaveBeenCalled()
    expect(await readFile(path, 'utf8')).toBe('A file blocks the directory')
  })
  it('resolves the active legacy backend path instead of guessing from the frontend profile', async () => {
    const { root, ports } = await fixture()
    ports.pluginDirectory.mockResolvedValue(join(root, 'Hoard', 'plugins'))
    await openDataFolder('logs', ports)
    expect(ports.openPath).toHaveBeenCalledExactlyOnceWith(join(root, 'Hoard', 'logs'))
  })
  it.each(['plugins', 'themes'])('preserves the existing %s folder behavior', async (folder) => {
    const { root, ports } = await fixture()
    await openDataFolder(folder, ports)
    expect(ports.openPath).toHaveBeenCalledExactlyOnceWith(join(root, folder))
    ports.openPath.mockResolvedValue('private shell problem')
    await expect(openDataFolder(folder, ports)).rejects.toThrow('The folder could not be opened.')
  })
  it('rejects unknown folders before querying the backend', async () => {
    const { ports } = await fixture()
    await expect(openDataFolder('../elsewhere', ports)).rejects.toThrow('Unknown Winnow folder')
    expect(ports.pluginDirectory).not.toHaveBeenCalled()
    expect(ports.openPath).not.toHaveBeenCalled()
  })
  it('keeps the existing unavailable-backend message without inventing a manual path', async () => {
    const { ports } = await fixture()
    await expect(
      openDataFolder('logs', { ...ports, pluginDirectory: async () => undefined }),
    ).rejects.toThrow('Connect to your library before opening its folder.')
    expect(ports.openPath).not.toHaveBeenCalled()
  })
})

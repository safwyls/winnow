// @vitest-environment jsdom
import { resolve } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { openInstallFolder } from '../src/main/install-folder'
import { InstallFolderButton } from '../src/renderer/features/install-folder'

afterEach(cleanup)
const folder = resolve('fixture-game')
function adapters(ownerships = [{ id: 1, installed: true, installPath: folder }]) {
  return {
    workspace: vi.fn().mockResolvedValue({ ownerships }),
    isDirectory: vi.fn().mockResolvedValue(true),
    openPath: vi.fn().mockResolvedValue(''),
  }
}
describe('installation directory boundary', () => {
  it('resolves the owned installed copy afresh and opens only its directory', async () => {
    const ports = adapters()
    await openInstallFolder(1, ports)
    expect(ports.workspace).toHaveBeenCalledTimes(1)
    expect(ports.isDirectory).toHaveBeenCalledWith(folder)
    expect(ports.openPath).toHaveBeenCalledWith(folder)
    ports.workspace.mockResolvedValue({ ownerships: [{ id: 1, installed: false, installPath: folder }] })
    await expect(openInstallFolder(1, ports)).rejects.toThrow('no longer installed')
    expect(ports.openPath).toHaveBeenCalledTimes(1)
  })
  it.each([null, 0, -1, 1.5, '1', folder, { id: 1 }, NaN, Infinity])(
    'refuses a renderer value which is not an ownership ID: %s',
    async (id) => {
      const ports = adapters()
      await expect(openInstallFolder(id, ports)).rejects.toThrow('Choose a game')
      expect(ports.workspace).not.toHaveBeenCalled()
      expect(ports.openPath).not.toHaveBeenCalled()
    },
  )
  it.each(['', ' ', 'file:///tmp/game', 'relative/game', `${folder}\u0000.exe`])(
    'refuses an absent or non-directory-shaped saved path: %s',
    async (installPath) => {
      const ports = adapters([{ id: 1, installed: true, installPath }])
      await expect(openInstallFolder(1, ports)).rejects.toThrow('no recorded installation folder')
      expect(ports.isDirectory).not.toHaveBeenCalled()
      expect(ports.openPath).not.toHaveBeenCalled()
    },
  )
  it('refuses deleted ownerships and executable files even when a saved path exists', async () => {
    const removed = adapters([])
    await expect(openInstallFolder(1, removed)).rejects.toThrow('no longer installed')
    const executable = adapters()
    executable.isDirectory.mockResolvedValue(false)
    await expect(openInstallFolder(1, executable)).rejects.toThrow('no longer available')
    expect(executable.openPath).not.toHaveBeenCalled()
  })
  it('keeps a missing or inaccessible directory out of the OS shell', async () => {
    const ports = adapters()
    ports.isDirectory.mockRejectedValue(new Error('ENOENT'))
    await expect(openInstallFolder(1, ports)).rejects.toThrow('no longer available')
    expect(ports.openPath).not.toHaveBeenCalled()
  })
  it('reports an OS refusal instead of claiming the folder opened', async () => {
    const ports = adapters()
    ports.openPath.mockResolvedValue('No application associated with this operation')
    await expect(openInstallFolder(1, ports)).rejects.toThrow('could not be opened')
  })
})

describe('installation directory control', () => {
  it.each([
    [false, folder],
    [undefined, folder],
    [true, null],
    [true, ' '],
  ] as const)('offers no folder when installed is %s and path is %s', (installed, installPath) => {
    render(<InstallFolderButton ownershipId={1} installed={installed} installPath={installPath} />)
    expect(screen.queryByRole('button')).toBeNull()
  })
  it('sends ownership identity only, keeps the action pending and permits retry after a refusal', async () => {
    let reject!: (error: Error) => void
    const open = vi
      .fn()
      .mockReturnValueOnce(
        new Promise<void>((_resolve, no) => {
          reject = no
        }),
      )
      .mockResolvedValue(undefined)
    Object.defineProperty(window, 'winnow', { configurable: true, value: { openInstallFolder: open } })
    render(<InstallFolderButton ownershipId={1} installed installPath={folder} />)
    const button = screen.getByRole('button', { name: 'Open install folder' }) as HTMLButtonElement
    fireEvent.click(button)
    expect(button.disabled).toBe(true)
    fireEvent.click(button)
    expect(open).toHaveBeenCalledExactlyOnceWith(1)
    reject(new Error('The installation folder could not be opened.'))
    expect((await screen.findByRole('alert')).textContent).toContain('could not be opened')
    await waitFor(() => expect(button.disabled).toBe(false))
    fireEvent.click(button)
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
    expect(open).toHaveBeenCalledTimes(2)
  })
})

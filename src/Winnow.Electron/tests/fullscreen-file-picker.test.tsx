// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mkdtemp, mkdir, readFile, readdir, rm, stat, writeFile } from 'node:fs/promises'
import { basename, dirname, join, resolve } from 'node:path'
import { tmpdir } from 'node:os'
import { FullscreenFilePickerService } from '../src/main/file-picker'
import { FullscreenFilePicker } from '../src/renderer/features/FullscreenFilePicker'
import type { FilePickerSnapshot } from '../src/shared/file-picker'

let directory: string
let service: FullscreenFilePickerService
beforeEach(async () => {
  directory = await mkdtemp(join(tmpdir(), 'winnow-fullscreen-picker-'))
  let receive: (snapshot: FilePickerSnapshot | null) => void = () => {}
  service = new FullscreenFilePickerService(
    (snapshot) => receive(snapshot),
    async () => [directory],
  )
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      onFilePicker: (callback: typeof receive) => {
        receive = callback
        return () => {
          receive = () => {}
        }
      },
      filePickerSnapshot: async () => service.snapshot,
      filePickerAction: (action: unknown) => service.action(action),
    },
  })
  HTMLElement.prototype.scrollIntoView = vi.fn()
})
afterEach(async () => {
  cleanup()
  service.cancel()
  if (
    dirname(resolve(directory)) !== resolve(tmpdir()) ||
    !basename(directory).startsWith('winnow-fullscreen-picker-')
  )
    throw Error('Refusing to remove an unrelated directory.')
  await rm(directory, { recursive: true, force: true })
})
async function choose(mode: 'open' | 'save' | 'directory', extensions = ['png'], name?: string) {
  let completion!: Promise<string | null>
  await act(async () => {
    completion = service.choose({
      title: mode === 'save' ? 'Export' : 'Choose cover',
      mode,
      extensions,
      initialDirectory: directory,
      suggestedName: name,
    })
  })
  await waitFor(() => expect(service.snapshot?.loading).toBe(false))
  return { completion }
}
function key(key: string) {
  fireEvent.keyDown(document.activeElement!, { key })
}
function accept() {
  fireEvent.click(document.activeElement!)
}

describe('fullscreen directory and filename chooser', () => {
  it('retains an opener that becomes disabled before the asynchronous chooser arrives', async () => {
    render(
      <>
        <button>Choose file</button>
        <FullscreenFilePicker />
      </>,
    )
    const opener = screen.getByRole('button', { name: 'Choose file' }) as HTMLButtonElement
    opener.focus()
    opener.disabled = true
    opener.blur()
    const { completion } = await choose('open')
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(await completion).toBeNull()
    await act(async () => {
      opener.disabled = false
    })
    await waitFor(() => expect(document.activeElement).toBe(opener))
  })

  it('a new picker stays usable and ignores failure from a cancelled prior action', async () => {
    await writeFile(join(directory, 'cover.PNG'), 'read-only selection fixture')
    let rejectOlder!: (error: Error) => void
    let delayed = false
    window.winnow.filePickerAction = async (action) => {
      if (action.action === 'entry' && !delayed) {
        delayed = true
        await new Promise<void>((_resolve, reject) => {
          rejectOlder = reject
        })
      } else await service.action(action)
    }
    render(<FullscreenFilePicker />)
    const prior = await choose('open')
    fireEvent.click(screen.getByRole('button', { name: 'File cover.PNG' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(await prior.completion).toBeNull()
    const next = await choose('open')
    await act(async () => rejectOlder(new Error('The old folder cannot be read.')))
    expect(screen.queryByRole('alert')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'File cover.PNG' }))
    expect(await next.completion).toBe(join(directory, 'cover.PNG'))
  })
  it('Back cancels an in-flight replacement check before it can complete the selected destination', async () => {
    const destination = join(directory, 'report.csv')
    await writeFile(destination, 'keep this until export succeeds')
    let resume!: () => void
    let inspections = 0
    service = new FullscreenFilePickerService(
      () => {},
      async () => [],
      async (path) => {
        if (++inspections === 2)
          await new Promise<void>((resolve) => {
            resume = resolve
          })
        return stat(path)
      },
    )
    const completion = service.choose({
      title: 'Export',
      mode: 'save',
      extensions: ['csv'],
      initialDirectory: directory,
    })
    const done = vi.fn()
    void completion.then(done)
    await waitFor(() => expect(service.snapshot?.loading).toBe(false))
    const id = service.snapshot!.id
    await service.action({ id, action: 'save', name: 'report.csv' })
    const replacing = service.action({ id, action: 'replace' })
    await waitFor(() => expect(resume).toBeTypeOf('function'))
    await service.action({ id, action: 'back' })
    resume()
    await replacing
    expect(service.snapshot?.replaceName).toBeUndefined()
    expect(done).not.toHaveBeenCalled()
    expect(await readFile(destination, 'utf8')).toBe('keep this until export succeeds')
    service.cancel()
    expect(await completion).toBeNull()
  })

  it('changing directory retires a pending file selection before filesystem inspection returns', async () => {
    const nested = join(directory, 'Art')
    await mkdir(nested)
    await writeFile(join(nested, 'cover.PNG'), 'read-only selection fixture')
    let resume!: () => void
    service = new FullscreenFilePickerService(
      () => {},
      async () => [],
      async (path) => {
        await new Promise<void>((resolve) => {
          resume = resolve
        })
        return stat(path)
      },
    )
    const completion = service.choose({
      title: 'Choose',
      mode: 'open',
      extensions: ['png'],
      initialDirectory: nested,
    })
    const done = vi.fn()
    void completion.then(done)
    await waitFor(() => expect(service.snapshot?.loading).toBe(false))
    const id = service.snapshot!.id
    const selecting = service.action({ id, action: 'entry', entryId: service.snapshot!.entries[0].id })
    await waitFor(() => expect(resume).toBeTypeOf('function'))
    await service.action({ id, action: 'parent' })
    resume()
    await selecting
    expect(service.snapshot?.directory).toBe(directory)
    expect(done).not.toHaveBeenCalled()
    service.cancel()
    expect(await completion).toBeNull()
  })
  it('does not reopen a cancelled chooser when its initial snapshot arrives late', async () => {
    let recover!: (snapshot: FilePickerSnapshot | null) => void
    window.winnow.filePickerSnapshot = () =>
      new Promise((resolve) => {
        recover = resolve
      })
    render(<FullscreenFilePicker />)
    const { completion } = await choose('open')
    const stale = service.snapshot
    await act(async () => service.cancel())
    expect(await completion).toBeNull()
    await act(async () => recover(stale))
    expect(screen.queryByRole('dialog')).toBeNull()
  })
  it('saving an existing file requires Cancel-first confirmation and selecting never writes the file', async () => {
    const destination = join(directory, 'report.csv')
    await writeFile(destination, 'keep this until export succeeds')
    render(
      <>
        <button>Export acquisitions</button>
        <FullscreenFilePicker />
      </>,
    )
    screen.getByRole('button', { name: 'Export acquisitions' }).focus()
    const { completion } = await choose('save', ['csv'], 'report.csv')
    const done = vi.fn()
    void completion.then(done)
    expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'File name' }))
    key('ArrowDown')
    accept()
    await screen.findByRole('heading', { name: 'Replace report.csv?' })
    expect(done).not.toHaveBeenCalled()
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Cancel' })))
    accept()
    await screen.findByRole('heading', { name: 'Export' })
    expect(done).not.toHaveBeenCalled()
    key('ArrowDown')
    accept()
    await screen.findByRole('heading', { name: 'Replace report.csv?' })
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Cancel' })))
    key('ArrowDown')
    accept()
    expect(await completion).toBe(destination)
    expect(await readFile(destination, 'utf8')).toBe('keep this until export succeeds')
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Export acquisitions' })),
    )
  })

  it('controller selects only allowed files case-insensitively and Back completes cancellation without changing bytes', async () => {
    const image = join(directory, 'cover.PNG')
    await writeFile(image, 'read-only selection fixture')
    await writeFile(join(directory, 'unrelated.exe'), 'never execute')
    render(<FullscreenFilePicker />)
    const { completion } = await choose('open')
    expect(screen.getByRole('button', { name: 'File cover.PNG' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /unrelated.exe/ })).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Parent folder' }))
    key('ArrowDown')
    accept()
    expect(await completion).toBe(image)
    expect(await readFile(image, 'utf8')).toBe('read-only selection fixture')
    expect(await readFile(join(directory, 'unrelated.exe'), 'utf8')).toBe('never execute')
    const cancelled = await choose('open')
    key('Escape')
    expect(await cancelled.completion).toBeNull()
  })

  it('browses directories and pages eight entries while stale and invented entry identities are refused', async () => {
    await mkdir(join(directory, 'Art'))
    for (let i = 0; i < 10; i++) await writeFile(join(directory, `cover-${i}.png`), 'image')
    const completion = service.choose({
      title: 'Choose',
      mode: 'open',
      extensions: ['png'],
      initialDirectory: directory,
    })
    await waitFor(() => expect(service.snapshot?.loading).toBe(false))
    const id = service.snapshot!.id
    expect(service.snapshot!.entries).toHaveLength(8)
    expect(service.snapshot!.entries[0].name).toBe('Art')
    await expect(
      service.action({ id, action: 'entry', entryId: join(directory, 'cover-0.png') }),
    ).rejects.toThrow('no longer available')
    await service.action({ id, action: 'next' })
    expect(service.snapshot!.entries).toHaveLength(3)
    await service.action({ id, action: 'previous' })
    await service.action({ id, action: 'entry', entryId: service.snapshot!.entries[0].id })
    expect(service.snapshot!.directory).toBe(join(directory, 'Art'))
    await service.action({ id, action: 'parent' })
    expect(service.snapshot!.directory).toBe(directory)
    await service.action({ id, action: 'cancel' })
    expect(await completion).toBeNull()
    await expect(service.action({ id, action: 'save', name: 'invented.csv' })).rejects.toThrow('has closed')
  })

  it.each([
    '../report.csv',
    'nested/report.csv',
    'file:report.csv',
    'CON.csv',
    'report.exe',
    '',
    'report.csv.',
  ])('rejects invalid save destination %j without creating a file', async (name) => {
    const completion = service.choose({
      title: 'Export',
      mode: 'save',
      extensions: ['csv'],
      initialDirectory: directory,
    })
    await waitFor(() => expect(service.snapshot?.loading).toBe(false))
    await expect(service.action({ id: service.snapshot!.id, action: 'save', name })).rejects.toThrow()
    expect(await readdir(directory)).toEqual([])
    service.cancel()
    expect(await completion).toBeNull()
  })

  it('returns a new destination with its extension without creating it and refuses folders with the same name', async () => {
    await mkdir(join(directory, 'taken.csv'))
    const completion = service.choose({
      title: 'Export',
      mode: 'save',
      extensions: ['csv'],
      initialDirectory: directory,
    })
    await waitFor(() => expect(service.snapshot?.loading).toBe(false))
    const id = service.snapshot!.id
    await expect(service.action({ id, action: 'save', name: 'taken.csv' })).rejects.toThrow('folder already')
    await service.action({ id, action: 'save', name: 'report' })
    expect(await completion).toBe(join(directory, 'report.csv'))
    expect(await readdir(directory)).toEqual(['taken.csv'])
  })
})

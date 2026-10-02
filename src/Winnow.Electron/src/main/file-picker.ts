import { randomUUID } from 'node:crypto'
import { readdir, stat } from 'node:fs/promises'
import type { Stats } from 'node:fs'
import { basename, dirname, extname, join, parse } from 'node:path'
import type { FilePickerAction, FilePickerSnapshot } from '../shared/file-picker'

export interface FilePickerOptions {
  title: string
  mode: 'open' | 'save' | 'directory'
  extensions?: string[]
  filterName?: string
  initialDirectory?: string
  suggestedName?: string
}
interface Entry {
  id: string
  name: string
  directory: boolean
  path: string
}
interface Session {
  options: FilePickerOptions
  snapshot: FilePickerSnapshot
  entries: Entry[]
  replacePath?: string
  generation: number
  actionGeneration: number
  resolve(path: string | null): void
}

async function readyRoots() {
  if (process.platform !== 'win32') return [parse(process.cwd()).root]
  const drives = await Promise.all(
    Array.from({ length: 26 }, async (_, index) => {
      const drive = `${String.fromCharCode(65 + index)}:\\`
      return await stat(drive).then(
        (value) => (value.isDirectory() ? drive : null),
        () => null,
      )
    }),
  )
  return drives.filter((value): value is string => value !== null)
}

/** Chooses a path only. Reads and writes remain in the named operation that opened it. */
export class FullscreenFilePickerService {
  private active?: Session
  constructor(
    private readonly publish: (value: FilePickerSnapshot | null) => void,
    private readonly roots = readyRoots,
    private readonly inspect: (path: string) => Promise<Stats> = stat,
  ) {}

  get snapshot(): FilePickerSnapshot | null {
    return this.active?.snapshot ?? null
  }

  choose(options: FilePickerOptions): Promise<string | null> {
    if (this.active) return Promise.resolve(null)
    return new Promise((resolve) => {
      const session: Session = {
        options,
        resolve,
        entries: [],
        generation: 0,
        actionGeneration: 0,
        snapshot: {
          id: randomUUID(),
          title: options.title,
          mode: options.mode,
          directory: options.initialDirectory ?? null,
          suggestedName: options.suggestedName,
          entries: [],
          page: 0,
          pages: 1,
          loading: true,
        },
      }
      this.active = session
      void this.read(session)
    })
  }

  cancel() {
    if (this.active) this.finish(this.active, null)
  }

  async action(input: unknown): Promise<void> {
    if (!input || typeof input !== 'object') throw Error('Invalid file chooser action.')
    const action = input as FilePickerAction
    const session = this.active
    if (!session || action.id !== session.snapshot.id) throw Error('This file chooser has closed.')
    const snapshot = session.snapshot
    const generation = ++session.actionGeneration
    const current = () => this.active === session && session.actionGeneration === generation
    if (action.action === 'cancel') return this.finish(session, null)
    if (action.action === 'back') {
      if (session.replacePath) {
        session.replacePath = undefined
        this.update(session, { replaceName: undefined })
      } else this.finish(session, null)
      return
    }
    if (snapshot.loading) return
    if (session.replacePath) {
      if (action.action !== 'replace') throw Error('Confirm or cancel replacing this file.')
      const path = session.replacePath
      const target = await this.inspect(path).catch(() => null)
      if (!current()) return
      if (target && !target.isFile()) throw Error('This destination is no longer a file.')
      this.finish(session, path)
      return
    }
    if (action.action === 'entry') {
      const entry = session.entries.find((entry) => entry.id === action.entryId)
      if (!entry) throw Error('This item is no longer available.')
      const value = await this.inspect(entry.path).catch(() => null)
      if (!current()) return
      if (entry.directory && value?.isDirectory()) {
        this.update(session, { directory: entry.path, page: 0 })
        await this.read(session)
      } else if (
        !entry.directory &&
        value?.isFile() &&
        session.options.mode === 'open' &&
        this.allowed(session, entry.path)
      ) {
        this.finish(session, entry.path)
      } else throw Error('This item is no longer available.')
      return
    }
    if (action.action === 'parent') {
      const current = snapshot.directory
      this.update(session, {
        directory: current && dirname(current) !== current ? dirname(current) : null,
        page: 0,
      })
      await this.read(session)
      return
    }
    if (action.action === 'previous' || action.action === 'next') {
      this.update(session, {
        page: Math.max(0, Math.min(snapshot.pages - 1, snapshot.page + (action.action === 'next' ? 1 : -1))),
      })
      this.page(session)
      return
    }
    if (action.action === 'directory' && session.options.mode === 'directory' && snapshot.directory) {
      const value = await this.inspect(snapshot.directory)
      if (!current()) return
      if (!value.isDirectory()) throw Error('This folder is no longer available.')
      this.finish(session, snapshot.directory)
      return
    }
    if (action.action === 'save' && session.options.mode === 'save' && snapshot.directory) {
      let name = typeof action.name === 'string' ? action.name.trim() : ''
      if (
        !name ||
        name === '.' ||
        name === '..' ||
        /[<>:"/\\|?*\u0000-\u001f]/.test(name) ||
        /[. ]$/.test(name) ||
        /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(name)
      )
        throw Error('Enter a file name without a folder path.')
      if (!extname(name) && session.options.extensions?.length)
        name += `.${session.options.extensions[0].replace(/^\./, '')}`
      if (!this.allowed(session, name)) throw Error('Choose a file name with an allowed extension.')
      const path = join(snapshot.directory, name)
      const value = await this.inspect(path).catch((error: NodeJS.ErrnoException) => {
        if (error.code === 'ENOENT') return null
        throw Error('This destination cannot be checked. Choose another folder.')
      })
      if (!current()) return
      if (value?.isDirectory()) throw Error('A folder already has that name. Choose another file name.')
      if (value && !value.isFile()) throw Error('Choose a regular file destination.')
      if (value) {
        session.replacePath = path
        this.update(session, { replaceName: name })
      } else this.finish(session, path)
      return
    }
    throw Error('Invalid file chooser action.')
  }

  private allowed(session: Session, path: string) {
    return (
      !session.options.extensions?.length ||
      session.options.extensions.some(
        (extension) => `.${extension.replace(/^\./, '').toLowerCase()}` === extname(path).toLowerCase(),
      )
    )
  }
  private update(session: Session, changes: Partial<FilePickerSnapshot>) {
    if (this.active !== session) return
    session.snapshot = { ...session.snapshot, ...changes }
    this.publish(session.snapshot)
  }
  private page(session: Session) {
    this.update(session, {
      entries: session.entries
        .slice(session.snapshot.page * 8, session.snapshot.page * 8 + 8)
        .map(({ id, name, directory }) => ({ id, name, directory })),
    })
  }
  private async read(session: Session) {
    const generation = ++session.generation
    this.update(session, { loading: true, error: undefined, entries: [] })
    try {
      const directory = session.snapshot.directory
      const entries: Entry[] = directory
        ? (await readdir(directory, { withFileTypes: true }))
            .filter(
              (entry) =>
                entry.isDirectory() ||
                (entry.isFile() && session.options.mode === 'open' && this.allowed(session, entry.name)),
            )
            .map((entry) => ({
              id: randomUUID(),
              name: entry.name,
              path: join(directory, entry.name),
              directory: entry.isDirectory(),
            }))
        : (await this.roots()).map((path) => ({
            id: randomUUID(),
            name: basename(path) || path,
            path,
            directory: true,
          }))
      if (this.active !== session || session.generation !== generation) return
      session.entries = entries.sort(
        (a, b) =>
          Number(b.directory) - Number(a.directory) ||
          a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
      )
      this.update(session, { loading: false, pages: Math.max(1, Math.ceil(entries.length / 8)) })
      this.page(session)
    } catch {
      if (session.generation === generation)
        this.update(session, { loading: false, error: 'This folder cannot be opened.', entries: [] })
    }
  }
  private finish(session: Session, path: string | null) {
    if (this.active !== session) return
    this.active = undefined
    this.publish(null)
    session.resolve(path)
  }
}

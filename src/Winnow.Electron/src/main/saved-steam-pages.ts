import { randomUUID } from 'node:crypto'
import { createReadStream } from 'node:fs'
import { stat } from 'node:fs/promises'
import { basename, extname } from 'node:path'
import type { SavedSteamPage, SavedSteamPageUpload } from '../shared/bridge'
import type { FilePickerOptions } from './file-picker'

const fileLimit = 64 * 1024 * 1024
const totalLimit = 128 * 1024 * 1024

/** Selection grants a temporary handle; only the explicit Read action opens file contents. */
export class SavedSteamPages {
  private selected = new Map<string, { path: string; name: string }>()
  private lifetime = new AbortController()
  private choosing = false

  constructor(
    private readonly chooseFile: (options: FilePickerOptions) => Promise<string | null>,
    private readonly cancelPicker: () => void,
  ) {}

  async choose(): Promise<SavedSteamPage | null> {
    if (this.choosing) return null
    const lifetime = this.lifetime
    this.choosing = true
    try {
      const path = await this.chooseFile({
        title: 'Choose a saved Steam page',
        mode: 'open',
        filterName: 'HTML pages',
        extensions: ['html', 'htm'],
      })
      if (!path || lifetime.signal.aborted) return null
      if (!['.html', '.htm'].includes(extname(path).toLowerCase()))
        throw new Error('Choose an HTML page (.html or .htm).')
      for (const [id, selected] of this.selected)
        if (selected.path.toLowerCase() === path.toLowerCase()) return { id, name: selected.name }
      const id = randomUUID()
      const name = basename(path)
      this.selected.set(id, { path, name })
      return { id, name }
    } finally {
      this.choosing = false
    }
  }

  async read(ids: unknown): Promise<SavedSteamPageUpload[]> {
    if (!Array.isArray(ids) || !ids.length || ids.some((id) => typeof id !== 'string'))
      throw new Error('Choose at least one saved page.')
    const files = [...new Set(ids as string[])].map((id) => {
      const selected = this.selected.get(id)
      if (!selected) throw new Error('Choose the saved pages again before reading them.')
      return selected
    })
    const { signal } = this.lifetime
    let selectedSize = 0
    for (const file of files) {
      signal.throwIfAborted()
      const info = await stat(file.path).catch(() => {
        throw new Error(`Could not read ${file.name}. Choose the page again and retry.`)
      })
      if (!info.isFile()) throw new Error('This selection is not a file.')
      if (info.size > fileLimit) throw new Error('Each saved page must be 64 MB or smaller.')
      selectedSize += info.size
      if (selectedSize > totalLimit) throw new Error('Choose no more than 128 MB of saved pages at once.')
    }
    let total = 0
    const uploads: SavedSteamPageUpload[] = []
    for (const file of files) {
      signal.throwIfAborted()
      try {
        let size = 0
        const chunks: Buffer[] = []
        for await (const chunk of createReadStream(file.path, { signal })) {
          const bytes = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk)
          size += bytes.length
          total += bytes.length
          if (size > fileLimit) throw new Error('Each saved page must be 64 MB or smaller.')
          if (total > totalLimit) throw new Error('Choose no more than 128 MB of saved pages at once.')
          chunks.push(bytes)
        }
        signal.throwIfAborted()
        uploads.push({ name: file.name, content: Buffer.concat(chunks).toString('base64') })
      } catch (error) {
        signal.throwIfAborted()
        if (error instanceof Error && !('code' in error)) throw error
        throw new Error(`Could not read ${file.name}. Choose the page again and retry.`)
      }
    }
    return uploads
  }

  clear(): void {
    this.lifetime.abort()
    this.lifetime = new AbortController()
    this.selected.clear()
    if (this.choosing) this.cancelPicker()
  }
}

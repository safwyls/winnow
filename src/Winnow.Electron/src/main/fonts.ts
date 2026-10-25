interface FontReader {
  getURL(): string
  isDestroyed(): boolean
  executeJavaScript(code: string, userGesture: boolean): Promise<unknown>
}

export function fontFamilies(value: unknown): string[] {
  if (!Array.isArray(value)) throw new Error('Installed fonts could not be read.')
  return [
    ...new Set(
      value
        .filter(
          (name): name is string =>
            typeof name === 'string' &&
            name.length > 0 &&
            name.length <= 200 &&
            !/[\u0000-\u001f\u007f{};<>]/.test(name),
        )
        .map((name) => name.trim())
        .filter(Boolean),
    ),
  ]
    .slice(0, 4096)
    .sort((a, b) => a.localeCompare(b))
}

/** Font permission exists only while the named catalogue request is in progress. */
export class FontCatalogue {
  private readonly pending = new Map<FontReader, Promise<string[]>>()
  constructor(private readonly trusted: (url: string) => boolean) {}
  allows(reader: FontReader | null, permission: string, origin: string, mainFrame: boolean): boolean {
    if (
      !reader ||
      !this.pending.has(reader) ||
      reader.isDestroyed() ||
      permission !== 'local-fonts' ||
      !mainFrame
    )
      return false
    const current = reader.getURL()
    if (!this.trusted(current)) return false
    const currentUrl = new URL(current)
    // Node treats Electron's registered standard scheme as opaque; Chromium sends
    // its actual scheme and authority when checking the top-level permission.
    const currentOrigin =
      currentUrl.protocol === 'winnow-app:' ? `${currentUrl.protocol}//${currentUrl.host}` : currentUrl.origin
    return origin === currentOrigin || origin === `${currentOrigin}/` || this.trusted(origin)
  }
  read(reader: FontReader): Promise<string[]> {
    if (reader.isDestroyed() || !this.trusted(reader.getURL()))
      return Promise.reject(new Error('Installed fonts are unavailable in this window.'))
    const existing = this.pending.get(reader)
    if (existing) return existing
    const url = reader.getURL()
    // Defer evaluation until permission has been registered in the pending map.
    const read = Promise.resolve()
      .then(async () => {
        const values = await reader.executeJavaScript(
          "(async()=>{if(typeof queryLocalFonts!=='function')throw Error('Installed fonts are unavailable');return (await queryLocalFonts()).map(font=>font.family)})()",
          true,
        )
        if (reader.isDestroyed() || reader.getURL() !== url)
          throw new Error('The window changed while reading installed fonts.')
        return fontFamilies(values)
      })
      .finally(() => this.pending.delete(reader))
    this.pending.set(reader, read)
    return read
  }
}

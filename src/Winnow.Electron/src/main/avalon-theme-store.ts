import { watch, type FSWatcher } from 'node:fs'
import { lstat, mkdir, open, readdir } from 'node:fs/promises'
import { join } from 'node:path'
import {
  MAX_AVALON_THEME_BYTES,
  MAX_AVALON_THEMES,
  parseAvalonTheme,
  type AvalonThemeCatalogue,
  type AvalonThemeDiagnostic,
} from '../shared/avalonThemeDocument'

export const EXAMPLE_AVALON_THEME = `// A Winnow theme. Copy this file, change its id, and edit the eight role colors.
// Flare marks unread updates, volt marks selection, and danger marks destructive actions.
// Optional structure, translucency, typography, defaults and overrides keep their original format.
${JSON.stringify({ schemaVersion: 1, id: 'winnow-copy', name: 'Winnow (copy)', reason: 'The house palette, ready to make your own.', seeds: { ground: '#0F1C1E', surface: '#16282A', text: '#F0EDE7', flare: '#FF4D93', volt: '#4DE8C2', amber: '#FFB63D', azure: '#57A8F0', danger: '#E04B45' } }, null, 2)}
`

const problem = (
  file: string,
  message: string,
  severity: 'error' | 'warning' = 'error',
): AvalonThemeDiagnostic => ({ file, field: '', message, severity })
/** Passive palette files share the backend's resolved data root, never the executable theme folder. */
export class AvalonThemeStore {
  private watcher?: FSWatcher
  private debounce?: ReturnType<typeof setTimeout>
  private preparing?: Promise<AvalonThemeDiagnostic[]>
  private disposed = false
  constructor(
    private readonly directory: () => Promise<string>,
    private readonly changed: () => void = () => {},
  ) {}

  async prepare(): Promise<AvalonThemeDiagnostic[]> {
    if (this.preparing) return this.preparing
    this.preparing = this.seedAndWatch()
    const diagnostics = await this.preparing
    if (diagnostics.length) this.preparing = undefined
    return diagnostics
  }
  private async seedAndWatch(): Promise<AvalonThemeDiagnostic[]> {
    try {
      const directory = await this.directory()
      await mkdir(directory, { recursive: true })
      if (!(await readdir(directory)).some((name) => /\.json$/i.test(name))) {
        try {
          const file = await open(join(directory, 'example.json'), 'wx')
          try {
            await file.writeFile(EXAMPLE_AVALON_THEME, 'utf8')
          } finally {
            await file.close()
          }
        } catch (error) {
          if ((error as NodeJS.ErrnoException).code !== 'EEXIST') throw error
        }
      }
      if (!this.watcher && !this.disposed) {
        try {
          this.watcher = watch(directory, { persistent: false }, (_event, name) => {
            if (name == null || /\.json$/i.test(name.toString())) this.nudge()
          })
          this.watcher.on('error', () => this.nudge())
        } catch {
          /* Manual reload remains available where file watching is unsupported. */
        }
      }
      return []
    } catch {
      return [
        problem(
          'themes folder',
          'The themes folder could not be prepared. Check its permissions and reload themes.',
          'warning',
        ),
      ]
    }
  }
  private nudge() {
    if (this.disposed) return
    clearTimeout(this.debounce)
    this.debounce = setTimeout(() => {
      if (!this.disposed) this.changed()
    }, 250)
    this.debounce.unref?.()
  }
  async load(): Promise<AvalonThemeCatalogue> {
    const result: AvalonThemeCatalogue = { themes: [], diagnostics: [] }
    let directory: string, files: string[]
    try {
      directory = await this.directory()
      files = (await readdir(directory, { withFileTypes: true }))
        .filter((entry) => entry.isFile() && /\.json$/i.test(entry.name))
        .map((entry) => entry.name)
        .sort((a, b) =>
          a.toUpperCase() < b.toUpperCase() ? -1 : a.toUpperCase() > b.toUpperCase() ? 1 : a < b ? -1 : 1,
        )
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT')
        result.diagnostics.push(
          problem(
            'themes folder',
            'The themes folder could not be read. Check its permissions and reload themes.',
            'warning',
          ),
        )
      return result
    }
    if (files.length > MAX_AVALON_THEMES)
      result.diagnostics.push(
        problem(
          'themes folder',
          `Only the first ${MAX_AVALON_THEMES} of ${files.length} JSON files by name are read.`,
          'warning',
        ),
      )
    const claimed = new Map<string, string>()
    for (const name of files.slice(0, MAX_AVALON_THEMES)) {
      try {
        const path = join(directory, name)
        const stat = await lstat(path)
        if (!stat.isFile() || stat.isSymbolicLink()) continue
        if (stat.size > MAX_AVALON_THEME_BYTES) {
          result.diagnostics.push(problem(name, 'Theme files cannot exceed 256 KB.'))
          continue
        }
        const file = await open(path, 'r')
        let text: string
        try {
          // A bounded read also covers a file growing between stat and open.
          const buffer = Buffer.alloc(MAX_AVALON_THEME_BYTES + 1)
          const { bytesRead } = await file.read(buffer, 0, buffer.length, 0)
          if (bytesRead > MAX_AVALON_THEME_BYTES) {
            result.diagnostics.push(problem(name, 'Theme files cannot exceed 256 KB.'))
            continue
          }
          text = buffer.subarray(0, bytesRead).toString('utf8')
        } finally {
          await file.close()
        }
        const parsed = parseAvalonTheme(name, text)
        result.diagnostics.push(...parsed.diagnostics)
        if (!parsed.document) continue
        const owner = claimed.get(parsed.document.id)
        if (owner) {
          result.diagnostics.push({
            ...problem(name, `This id is already used by ${owner}. Give each theme its own id.`),
            field: 'id',
          })
          continue
        }
        claimed.set(parsed.document.id, name)
        result.themes.push({ file: name, document: parsed.document })
      } catch {
        result.diagnostics.push(
          problem(name, 'This theme file could not be read. Other themes are still available.'),
        )
      }
    }
    return result
  }
  async export(text: unknown): Promise<{ file: string | null; diagnostics: AvalonThemeDiagnostic[] }> {
    if (typeof text !== 'string')
      return { file: null, diagnostics: [problem('export', 'Expected a JSON theme document.')] }
    const parsed = parseAvalonTheme('export', text)
    if (!parsed.document) return { file: null, diagnostics: parsed.diagnostics }
    try {
      const directory = await this.directory()
      await mkdir(directory, { recursive: true })
      for (let suffix = 1; suffix <= 10000; suffix++) {
        const name = `${parsed.document.id}${suffix === 1 ? '' : `-${suffix}`}.json`
        try {
          const file = await open(join(directory, name), 'wx')
          try {
            await file.writeFile(JSON.stringify(parsed.document, null, 2) + '\n', 'utf8')
          } finally {
            await file.close()
          }
          this.nudge()
          return { file: name, diagnostics: parsed.diagnostics }
        } catch (error) {
          if ((error as NodeJS.ErrnoException).code !== 'EEXIST') throw error
        }
      }
      throw new Error('No unused filename')
    } catch {
      return {
        file: null,
        diagnostics: [
          problem(
            'themes folder',
            'The theme could not be exported. Check folder permissions and available space.',
          ),
        ],
      }
    }
  }
  dispose() {
    this.disposed = true
    clearTimeout(this.debounce)
    this.watcher?.close()
    this.watcher = undefined
  }
}

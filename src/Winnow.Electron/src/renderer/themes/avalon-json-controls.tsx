import { useMemo, useState } from 'react'
import { resolvedTypography } from '../../shared/theme'
import type { ThemeRuntime } from '../theming/runtime'
import type { AvalonThemeDiagnostic } from '../../shared/avalonThemeDocument'
import { avalonPalette } from './avalon-palettes'
import { avalonThemeReport, avalonThemeDiagnostics, exportAvalonPalette } from './avalon-json'

function ThemeProblems({ items }: { items: AvalonThemeDiagnostic[] }) {
  return (
    <ul>
      {items.map((item, index) => (
        <li key={`${item.file}:${item.field}:${index}`}>
          <strong>
            {item.file}
            {item.field ? ` · ${item.field}` : ''}
          </strong>
          : {item.severity === 'error' ? 'Could not load. ' : ''}
          {item.message}
        </li>
      ))}
    </ul>
  )
}

export function AvalonJsonControls({ runtime }: { runtime: ThemeRuntime }) {
  const [busy, setBusy] = useState(false),
    [notice, setNotice] = useState('')
  const palette = avalonPalette(String(runtime.profile.settings.avalon?.palette ?? 'winnow'))
  const catalogue = runtime.avalonCatalogue
  const diagnostics = useMemo(() => avalonThemeDiagnostics(catalogue), [catalogue])
  const report = useMemo(() => (palette ? avalonThemeReport(palette) : null), [palette])
  const errors = diagnostics.filter((item) => item.severity === 'error')
  const warnings = diagnostics.filter((item) => item.severity === 'warning')
  const run = async (action: () => Promise<void>) => {
    setBusy(true)
    setNotice('')
    try {
      await action()
    } catch {
      setNotice('The themes folder could not be accessed. Check its permissions and try again.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <section className="studio-panel" aria-labelledby="avalon-json-heading">
      <h2 id="avalon-json-heading">Authored palettes</h2>
      <p className="reading-prose">
        Edit JSON files in your library’s themes folder to change the original Winnow color roles and
        typography. Saved edits reload automatically.
      </p>
      {palette?.document?.reason && <p className="reading-prose">{palette.document.reason}</p>}
      {report && <p className="reading-prose">{report.headline}</p>}
      <div className="inline-actions">
        <button
          disabled={busy}
          onClick={() =>
            void run(async () => {
              await window.winnow.openDataFolder?.('themes')
            })
          }
        >
          Open themes folder
        </button>
        <button
          disabled={busy}
          onClick={() =>
            void run(async () => {
              await runtime.reloadAvalonThemes?.()
              setNotice('Authored palettes reloaded.')
            })
          }
        >
          Reload authored palettes
        </button>
        <button
          disabled={busy || !palette || !window.winnow.exportAvalonTheme}
          onClick={() =>
            void run(async () => {
              if (!palette) return
              const result = await window.winnow.exportAvalonTheme!(
                exportAvalonPalette(palette, resolvedTypography(runtime.profile)),
              )
              if (!result.file) {
                setNotice(result.diagnostics.map((item) => item.message).join(' '))
                return
              }
              await runtime.reloadAvalonThemes?.()
              setNotice(
                `Exported ${result.file}. Existing files were kept. Change the copy’s id when making a new palette.`,
              )
            })
          }
        >
          Export palette as JSON
        </button>
      </div>
      {notice && <p role="status">{notice}</p>}
      {!!errors.length && (
        <section aria-label="Theme file errors">
          <ThemeProblems items={errors} />
        </section>
      )}
      {!!warnings.length && (
        <details>
          <summary>Some themes may affect legibility.</summary>
          <ThemeProblems items={warnings} />
        </details>
      )}
    </section>
  )
}

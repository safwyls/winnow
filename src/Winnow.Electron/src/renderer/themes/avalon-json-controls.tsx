import { useMemo, useState } from 'react'
import { resolvedTypography } from '../../shared/theme'
import type { ThemeRuntime } from '../theming/runtime'
import { avalonPalette, deriveAvalonPalette } from './avalon-palettes'
import { avalonThemeReport, exportAvalonPalette, inspectAvalonTheme } from './avalon-json'

export function AvalonJsonControls({ runtime }: { runtime: ThemeRuntime }) {
  const [busy, setBusy] = useState(false),
    [notice, setNotice] = useState('')
  const palette = avalonPalette(String(runtime.profile.settings.avalon?.palette ?? 'winnow'))
  const catalogue = runtime.avalonCatalogue
  const diagnostics = useMemo(
    () => [
      ...(catalogue?.diagnostics ?? []),
      ...(catalogue?.themes.flatMap(({ file, document }) =>
        inspectAvalonTheme({ ...deriveAvalonPalette(document), sourceFile: file }),
      ) ?? []),
    ],
    [catalogue],
  )
  const report = useMemo(() => (palette ? avalonThemeReport(palette) : null), [palette])
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
      {!!diagnostics.length && (
        <details>
          <summary>
            {diagnostics.length} palette {diagnostics.length === 1 ? 'note' : 'notes'}
          </summary>
          <ul>
            {diagnostics.map((item, index) => (
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
        </details>
      )}
    </section>
  )
}

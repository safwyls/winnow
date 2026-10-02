import '../../src/renderer/styles.css'
import '../../src/renderer/themes/avalon.css'
import '../../src/renderer/theming/studio.css'
import { useLayoutEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { flushSync } from 'react-dom'
import { DEFAULT_PROFILE, typographyKey } from '../../src/shared/theme'
import { DEFAULT_TYPOGRAPHY } from '../../src/shared/typography'
import { applyThemeProfile } from '../../src/renderer/theming/runtime'
import { AvalonAction } from '../../src/renderer/themes/avalon-actions'

const parameters = new URLSearchParams(location.search)
const kind = parameters.get('kind') ?? 'desktop'
const palette = parameters.get('palette') ?? 'winnow'
const api = { size: (_value: number) => {}, selected: (_value: number | null) => {} }
Object.assign(window, { typographyRuntimeProbe: api })

function Probe() {
  const [percent, setPercent] = useState(100)
  const [selected, setSelected] = useState<number | null>(null)
  api.size = (value) => flushSync(() => setPercent(value))
  api.selected = (value) => flushSync(() => setSelected(value))
  useLayoutEffect(() => {
    const profile = structuredClone(DEFAULT_PROFILE)
    profile.settings.avalon = { palette }
    profile.appearance.reducedMotion = true
    profile.appearance.typography = {
      [typographyKey(profile)]: {
        ...DEFAULT_TYPOGRAPHY,
        interfaceFont: 'IBM Plex Mono',
        sizePercent: percent,
      },
    }
    applyThemeProfile(profile)
  }, [percent])
  if (kind === 'fullscreen')
    return (
      <main
        className="avalon-shell fullscreen"
        style={{ display: 'block', padding: 20, '--fullscreen-text-scale': '1.4' } as React.CSSProperties}
      >
        <h1>Fullscreen heading</h1>
        <p
          data-source-copy
          style={{ fontSize: 'calc(28px * var(--theme-text-scale) * var(--fullscreen-text-scale))' }}
        >
          Fullscreen copy
        </p>
        <div className="avalon-system-status">
          <span className="avalon-clock">12:34</span>
        </div>
        <svg data-source-icon width="32" height="32" style={{ width: 32, height: 32 }}>
          <path d="M4 4h24v24H4z" fill="currentColor" />
        </svg>
        <div className="avalon-actions-panel" style={{ position: 'relative', width: 700 }}>
          <div className="avalon-actions-body">
            <AvalonAction label="Open game" onChoose={() => {}} />
          </div>
        </div>
      </main>
    )
  if (kind === 'inputs')
    return (
      <main style={{ padding: 20, display: 'flex', flexDirection: 'column', gap: 16 }}>
        <button>Choose</button>
        <input aria-label="Name" defaultValue="Name" />
        <select aria-label="Choice" defaultValue="Choice">
          <option>Choice</option>
        </select>
        <label>
          <input type="checkbox" />
          Enabled
        </label>
        <button role="switch" aria-checked="false">
          On
        </button>
        <input type="number" aria-label="Number" defaultValue={10} />
      </main>
    )
  if (kind === 'segments')
    return (
      <main className="avalon-shell desktop" style={{ display: 'block', background: 'var(--surface)' }}>
        <div
          className="segmented"
          data-source-segments
          style={{
            position: 'absolute',
            left: 20,
            top: 20,
            width: 392,
            height: 120,
            borderWidth: 4,
            borderRadius: 16,
            gap: 0,
            padding: 0,
          }}
        >
          {['First', 'Middle', 'Last'].map((label, index) => (
            <button
              key={label}
              aria-pressed={selected === index}
              style={{ width: 128, height: 112, flex: 'none' }}
            >
              {label}
            </button>
          ))}
        </div>
      </main>
    )
  return (
    <main className="avalon-shell desktop" style={{ display: 'block', padding: 20 }}>
      <h1>Your library</h1>
      <section className="studio-panel">
        <p>A paragraph that stays readable.</p>
        <span className="studio-help" data-compact>
          Compact label
        </span>
        <button className="primary" style={{ width: 120 }}>
          Apply
        </button>
      </section>
    </main>
  )
}
createRoot(document.getElementById('root')!).render(<Probe />)

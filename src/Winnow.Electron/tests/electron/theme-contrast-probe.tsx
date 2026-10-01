import '../../src/renderer/styles.css'
import '../../src/renderer/themes/avalon.css'
import { useLayoutEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { flushSync } from 'react-dom'
import { DEFAULT_PROFILE } from '../../src/shared/theme'
import { applyThemeProfile } from '../../src/renderer/theming/runtime'
import { ConfirmationDialog } from '../../src/renderer/features/ConfirmationDialog'
import { AvalonAction, AvalonActions } from '../../src/renderer/themes/avalon-actions'
import { LibraryCutBar } from '../../src/renderer/themes/avalon-library-chrome'

type Kind = 'actions' | 'destructive' | 'selected' | 'inputs' | 'fullscreen'
const params = new URLSearchParams(location.search)
const initialKind = (params.get('kind') ?? 'actions') as Kind
const state = {
  configure: (_options: { palette?: string; kind?: Kind; disabled?: boolean; current?: boolean }) => {},
  activations: 0,
}
Object.assign(window, { themeContrastProbe: state })

function Probe() {
  const [palette, setPalette] = useState(params.get('palette') ?? 'winnow')
  const [kind, setKind] = useState<Kind>(initialKind)
  const [disabled, setDisabled] = useState(false)
  const [current, setCurrent] = useState(false)
  const [confirm, setConfirm] = useState(initialKind === 'destructive')
  state.configure = (patch) =>
    flushSync(() => {
      if (patch.palette) setPalette(patch.palette)
      if (patch.kind) {
        setKind(patch.kind)
        setConfirm(patch.kind === 'destructive')
      }
      if (patch.disabled != null) setDisabled(patch.disabled)
      if (patch.current != null) setCurrent(patch.current)
    })
  useLayoutEffect(() => {
    const profile = structuredClone(DEFAULT_PROFILE)
    profile.settings.avalon = { palette }
    profile.appearance.scale = 100
    profile.appearance.reducedMotion = true
    applyThemeProfile(profile)
    document.documentElement.dataset.mode = kind === 'fullscreen' ? 'fullscreen' : 'desktop'
  }, [palette, kind])
  const choose = () => {
    state.activations++
  }
  return (
    <div
      className={`avalon-shell ${kind === 'fullscreen' ? 'fullscreen' : 'desktop'}`}
      style={{
        display: 'block',
        position: 'relative',
        height: '100%',
        background: kind === 'fullscreen' ? 'var(--bg)' : 'var(--surface)',
      }}
    >
      <main
        className="avalon-content"
        style={{
          height: '100%',
          padding: kind === 'inputs' || kind === 'selected' ? 24 : 0,
          display: 'flex',
          flexDirection: kind === 'inputs' ? 'column' : 'row',
          gap: 24,
          alignItems: kind === 'actions' ? 'center' : 'flex-start',
          justifyContent: kind === 'actions' ? 'center' : 'flex-start',
          background: 'var(--surface)',
        }}
      >
        {kind === 'actions' && (
          <>
            <button className="primary" disabled={disabled} onClick={choose}>
              Save changes
            </button>
            <button className="secondary" disabled={disabled} onClick={choose}>
              Cancel
            </button>
          </>
        )}
        {kind === 'destructive' && (
          <ConfirmationDialog
            open={confirm}
            onOpenChange={setConfirm}
            trigger={<button>Delete list…</button>}
            title="Delete this list?"
            description="Its games will stay in your library."
            confirmLabel="Delete list"
            cancelLabel="Cancel"
            pending={disabled}
            onConfirm={choose}
          />
        )}
        {kind === 'selected' && (
          <>
            <div className="segmented">
              <button aria-pressed="true">Tracked sessions</button>
            </div>
            <LibraryCutBar
              games={[]}
              visible={0}
              facts={new Map()}
              state={
                {
                  filter: { stores: ['steam'] },
                  list: null,
                  base: null,
                  applyFilter() {},
                  selectList() {},
                } as unknown as Parameters<typeof LibraryCutBar>[0]['state']
              }
            />
          </>
        )}
        {kind === 'inputs' && (
          <>
            <input aria-label="Library search" defaultValue="Search your library" style={{ width: 320 }} />
            <select aria-label="Library sort" defaultValue="Recently played" style={{ width: 320 }}>
              <option>Recently played</option>
              <option>Title</option>
            </select>
          </>
        )}
      </main>
      {kind === 'fullscreen' && (
        <AvalonActions open title="Control contrast" close={() => {}}>
          <h2>Your next game</h2>
          <AvalonAction
            label="Open game"
            disabled={disabled}
            current={current}
            closeOnChoose={false}
            onChoose={choose}
          />
        </AvalonActions>
      )}
    </div>
  )
}
createRoot(document.getElementById('root')!).render(<Probe />)

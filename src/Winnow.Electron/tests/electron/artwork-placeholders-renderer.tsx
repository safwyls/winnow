import '../../src/renderer/styles.css'
import '../../src/renderer/themes/avalon.css'
import '../../src/renderer/themes/rift/rift.css'
import { createRoot } from 'react-dom/client'
import { useState } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Artwork } from '../../src/renderer/components/Artwork'
import { ArtworkAsset } from '../../src/renderer/components/ArtworkAsset'
import { ArtworkImage } from '../../src/renderer/features/artwork-browser'
import { IgdbCover } from '../../src/renderer/features/igdb-candidate'
import { AvalonBackdrop } from '../../src/renderer/themes/avalon-backdrop'
import { avalon } from '../../src/renderer/themes/avalon'
import { afterglow } from '../../src/renderer/themes/afterglow'
import { rift } from '../../src/renderer/themes/rift'
import { catalogue } from '../../src/renderer/themes/catalogue'
import { DEFAULT_PROFILE, selectThemeProfile } from '../../src/shared/theme'
import { applyThemeProfile } from '../../src/renderer/theming/runtime'
import type { WinnowBridge } from '../../src/shared/bridge'

const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
window.winnow = {
  request: async () => ({
    ok: true,
    status: 200,
    data: { current: null, candidates: [], coverKey: null, revision: 'no-artwork' },
  }),
  artwork: async () => null,
  cancelRequest: async () => true,
  onEvent: () => () => {},
} as unknown as WinnowBridge
let changeMode: (mode: 'desktop' | 'fullscreen') => void = () => {}
const configure = (themeId: string, mode: 'desktop' | 'fullscreen') => {
  const theme = [avalon, afterglow, rift, catalogue].find((value) => value.id === themeId)!
  const profile = selectThemeProfile(structuredClone(DEFAULT_PROFILE), themeId, theme)
  applyThemeProfile(profile)
  document.documentElement.dataset.mode = mode
  document.documentElement.style.setProperty('--effective-interface-scale', '1')
  changeMode(mode)
}
Object.assign(window, { placeholderFixture: { configure } })
configure('avalon', 'desktop')
function Fixture() {
  const [mode, setMode] = useState<'desktop' | 'fullscreen'>('desktop')
  changeMode = setMode
  return (
    <QueryClientProvider client={client}>
      <main
        style={{
          padding: 32,
          display: 'grid',
          gap: 24,
          gridTemplateColumns: '240px 1fr',
          height: '100%',
          overflow: 'auto',
        }}
      >
        <button
          aria-label="Open A distant island"
          className="avalon-cover"
          style={{ position: 'relative', width: 240, height: 360, padding: 0 }}
        >
          <Artwork workId={1} />
          <span className="avalon-cover-fallback">A distant island</span>
        </button>
        <section style={{ display: 'grid', gap: 20 }}>
          <div data-fixture="hero" style={{ position: 'relative', height: 180 }}>
            <Artwork workId={1} hero eager className="fixture-hero" />
          </div>
          <div data-fixture="backdrop" style={{ position: 'relative', height: 180 }}>
            <AvalonBackdrop workId={1} fullscreen={mode === 'fullscreen'} />
          </div>
        </section>
        <div data-fixture="screenshot" style={{ position: 'relative', height: 135 }}>
          <ArtworkAsset asset={{ provider: 'igdb-shot', id: 'none' }} width={400} alt="Game screenshot" />
        </div>
        <div data-fixture="preview" style={{ height: 135 }}>
          <ArtworkImage preview />
        </div>
        <div data-fixture="match">
          <IgdbCover />
        </div>
        <style>{`.fixture-hero{height:100%;width:100%}.avalon-cover-fallback{pointer-events:none}`}</style>
      </main>
    </QueryClientProvider>
  )
}
createRoot(document.getElementById('root')!).render(<Fixture />)

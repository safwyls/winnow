import '../../src/renderer/styles.css'
import { useState, type CSSProperties } from 'react'
import { createRoot } from 'react-dom/client'
import { AvalonCover } from '../../src/renderer/themes/avalon'
import { AvalonCoverWorkspace } from '../../src/renderer/themes/avalon-desktop-cover'
import { AVALON_PALETTES, avalonPaletteStyle } from '../../src/renderer/themes/avalon-palettes'
import type { ThemeContext } from '../../src/shared/theme'
import { coverGame, coverProfile, coverWorkspace, FixtureCoverArt } from '../cover-fixtures'

interface Options {
  width: number
  reduced: boolean
  installed: boolean
  played: boolean
  singleStore: boolean
  selected: boolean
  expansions: number
  attached: boolean
  workId: number
  fullscreen: boolean
}
const defaults: Options = {
  width: 148,
  reduced: true,
  installed: true,
  played: true,
  singleStore: false,
  selected: false,
  expansions: 0,
  attached: true,
  workId: 1,
  fullscreen: false,
}
const probe = {
  opened: [] as number[],
  launches: [] as number[],
  configure: (_value: Partial<Options>) => {},
  keys: [] as string[],
}
Object.assign(window, {
  coverProbe: probe,
  winnow: {
    request: async () => ({ ok: true, status: 200, data: { ratings: [] } }),
  },
})
function Probe() {
  const [options, setOptions] = useState(defaults)
  probe.configure = (patch) => setOptions((state) => ({ ...state, ...patch }))
  const game = coverGame(options.workId, options.installed, options.played, options.singleStore)
  const profile = coverProfile()
  profile.appearance.reducedMotion = options.reduced
  const context = {
    mode: options.fullscreen ? 'fullscreen' : 'desktop',
    profile,
    components: { Artwork: FixtureCoverArt },
    actions: {
      async launch(id: number) {
        probe.launches.push(id)
      },
    },
    openGame(id: number) {
      probe.opened.push(id)
    },
  } as unknown as ThemeContext
  return (
    <div
      className={`avalon-shell ${context.mode}`}
      data-reduced-motion={options.reduced}
      style={
        {
          ...avalonPaletteStyle(AVALON_PALETTES[0]!),
          display: 'block',
          padding: 40,
          '--font-body': '"Avalon Body"',
          '--font-display': '"Avalon Display"',
          '--font-mono': '"Avalon Data"',
        } as CSSProperties
      }
    >
      <button id="outside" style={{ position: 'absolute', left: 800, top: 450 }}>
        Outside
      </button>
      <div id="slot" data-width={options.width} style={{ width: options.width, margin: 40 }}>
        <AvalonCoverWorkspace.Provider value={coverWorkspace(game)}>
          {options.attached && (
            <AvalonCover
              context={context}
              game={game}
              selected={options.selected}
              expansion={
                options.expansions
                  ? {
                      count: options.expansions,
                      text: `${options.expansions} expansions`,
                      unplayed: true,
                    }
                  : undefined
              }
              onKeyDown={(event) => probe.keys.push(event.key)}
            />
          )}
        </AvalonCoverWorkspace.Provider>
      </div>
    </div>
  )
}
createRoot(document.getElementById('root')!).render(<Probe />)

import '../../src/renderer/styles.css'
import '@fontsource/dm-sans/400.css'
import '@fontsource/dm-sans/500.css'
import '@fontsource/dm-sans/600.css'
import '@fontsource/newsreader/400.css'
import '@fontsource/newsreader/400-italic.css'
import '@fontsource/jetbrains-mono/400.css'
import { createRoot } from 'react-dom/client'
import { flushSync } from 'react-dom'
import { createDesignFixture, installDesignFixture } from '../preview/design-fixture'
import { DESIGN_SURFACES, DesignPreview, type DesignSurface } from '../preview/design-preview'

const parameters = new URLSearchParams(location.search)
const mode = parameters.get('mode') === 'fullscreen' ? 'fullscreen' : 'desktop'
const nativeDate = Date
const epoch = nativeDate.parse('2026-12-01T12:00:00Z')
// Anchor the original relative dates without freezing timers or render frames.
window.Date = new Proxy(nativeDate, {
  construct(target, args, newTarget) {
    return Reflect.construct(target, args.length ? args : [epoch], newTarget)
  },
  apply() {
    return new nativeDate(epoch).toString()
  },
  get(target, property, receiver) {
    return property === 'now' ? () => epoch : Reflect.get(target, property, receiver)
  },
})
const fixture = createDesignFixture({ mode, now: new Date() })
const restore = installDesignFixture(fixture)
const container = document.getElementById('root')!
const root = createRoot(container)
type Surface = DesignSurface

function show(surface: Surface) {
  if (surface !== 'Shell' && !(DESIGN_SURFACES as readonly string[]).includes(surface))
    throw Error(`Unknown production preview surface: ${surface}`)
  flushSync(() => root.render(<DesignPreview key={surface} fixture={fixture} surface={surface} />))
  container.dataset.previewSurface = surface
}

Object.assign(window, {
  designPreviewNative: {
    show,
    state: () => ({
      mode,
      now: new Date().toISOString(),
      surfaces: DESIGN_SURFACES,
      games: fixture.games,
      workspace: fixture.workspace,
      feed: fixture.feed,
      details: fixture.details(4),
      requests: fixture.requests,
      blocked: fixture.blocked,
      writes: fixture.writes,
    }),
  },
})
window.addEventListener(
  'pagehide',
  () => {
    root.unmount()
    restore()
  },
  { once: true },
)
show((parameters.get('surface') ?? 'Shell') as Surface)

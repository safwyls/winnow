import '../../src/renderer/styles.css'
import { createRoot } from 'react-dom/client'
import { createDesignFixture, installDesignFixture } from './design-fixture'
import { DESIGN_SURFACES, DesignPreview, type DesignSurface } from './design-preview'

const query = new URLSearchParams(location.search)
const fixture = createDesignFixture({ mode: query.get('mode') === 'fullscreen' ? 'fullscreen' : 'desktop' })
const requested = query.get('surface')
const surface: DesignSurface = DESIGN_SURFACES.find((name) => name === requested) ?? 'Shell'
installDesignFixture(fixture)
createRoot(document.getElementById('root')!).render(<DesignPreview fixture={fixture} surface={surface} />)

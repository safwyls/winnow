import '../../src/renderer/styles.css'
import { useState, type CSSProperties } from 'react'
import { createRoot } from 'react-dom/client'
import { AvalonPreviewBubble } from '../../src/renderer/themes/avalon-preview'
import '../../src/renderer/themes/avalon.css'

// The source control test supplies a 200 by 100 layout slot and brushes directly.
// This host supplies the same inputs to the production bubble, including its CSS.
const source = document.createElement('canvas')
source.width = source.height = 1
source.getContext('2d')!.fillStyle = 'red'
source.getContext('2d')!.fillRect(0, 0, 1, 1)
const red = source.toDataURL('image/png')
function Probe() {
  const [flipped, flip] = useState(false)
  Object.assign(window, { previewPixelProbe: { flip: () => flip(true) } })
  const artwork = new URLSearchParams(location.search).get('kind') === 'artwork'
  return (
    <AvalonPreviewBubble
      aria-label="Source pixel bubble"
      arrowOffset={40}
      arrowOnRight={flipped}
      artwork={artwork ? <img src={red} alt="" style={{ width: '100%', height: '100%' }} /> : undefined}
      style={
        {
          width: 200,
          height: 100,
          left: 0,
          top: 0,
          '--preview-background': 'black',
          '--preview-border': artwork ? 'white' : 'transparent',
        } as CSSProperties
      }
    >
      <div data-source-child="true" />
    </AvalonPreviewBubble>
  )
}
document.documentElement.style.background = 'transparent'
Object.assign(document.body.style, { margin: '0', background: 'transparent', zoom: '1' })
createRoot(document.getElementById('root')!).render(<Probe />)

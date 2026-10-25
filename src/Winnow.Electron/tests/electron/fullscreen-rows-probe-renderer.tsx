import '../../src/renderer/styles.css'
import '../../src/renderer/themes/avalon.css'
import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { flushSync } from 'react-dom'
import { AvalonRowViewport } from '../../src/renderer/themes/avalon-row-viewport'
import { AvalonShelfIndicator } from '../../src/renderer/themes/avalon-shelf-indicator'

const defaults = { indicator: false, titles: ['Patched', 'Forgotten', 'Unplayed'], selected: 1, width: 200 }
const rows = Array.from({ length: 8 }, (_, index) => ({ id: `row-${index}` }))
const browserFrame = window.requestAnimationFrame.bind(window)
let manual = false
let callbacks: FrameRequestCallback[] = []
window.requestAnimationFrame = (callback) => {
  if (!manual) return browserFrame(callback)
  callbacks.push(callback)
  return callbacks.length
}
const probe = {
  configure: (_value: Partial<typeof defaults>) => {},
  select: (_value: number) => {},
  selections: [] as number[],
  manual: () => {
    manual = true
  },
  frame: (timestamp: number) => {
    const pending = callbacks
    callbacks = []
    flushSync(() => pending.forEach((callback) => callback(timestamp)))
  },
}
Object.assign(window, { rowProbe: probe })
function Probe() {
  const [options, setOptions] = useState(defaults)
  const [first, setFirst] = useState(0)
  probe.configure = (patch) => flushSync(() => setOptions((value) => ({ ...value, ...patch })))
  probe.select = (value) => flushSync(() => setFirst(value))
  return (
    <>
      <button style={{ position: 'absolute', left: 900, top: 20 }} id="outside">
        Outside
      </button>
      {options.indicator ? (
        <div style={{ width: options.width, height: 400, display: 'flex' }}>
          <AvalonShelfIndicator
            titles={options.titles}
            selected={options.selected}
            onSelect={(index) => probe.selections.push(index)}
          />
        </div>
      ) : (
        <div style={{ width: 800, height: 600, display: 'grid' }}>
          <AvalonRowViewport rows={rows} first={first} reducedMotion={false}>
            {(row) => <button>{row.id}</button>}
          </AvalonRowViewport>
        </div>
      )}
    </>
  )
}
createRoot(document.getElementById('root')!).render(<Probe />)

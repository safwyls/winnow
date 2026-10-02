import '../../src/renderer/styles.css'
import '../../src/renderer/themes/avalon.css'
import { useEffect, useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { AvalonAction, AvalonActions } from '../../src/renderer/themes/avalon-actions'
import { controllerScope, useController } from '../../src/renderer/controller'

const probe = { attached: 0, detached: 0, chosen: 0, disabled: 0, nested: 0, leaked: 0 }
Object.assign(window, { actionProbe: probe })
const labels = [
  'Choose artwork',
  'Edit game details',
  'Choose launch version',
  'Open installation folder',
  'View recorded sessions',
  'Manage store links',
  'Add to a collection',
  'Hide this game',
  'Open store page',
  'Show related games',
  'Manage launch options',
  'Choose controller layout',
  'View achievements',
  'Close',
]
function Origin() {
  useEffect(() => {
    probe.attached++
    return () => {
      probe.detached++
    }
  }, [])
  return (
    <>
      <h1>Hollow Knight</h1>
      <p>Steam · 42.8 h played</p>
      <p>Explore a vast ruined kingdom of insects and heroes.</p>
    </>
  )
}
function Fixture() {
  const [open, setOpen] = useState(false)
  const [nested, setNested] = useState(false)
  const [many, setMany] = useState(false)
  const trigger = useRef<HTMLButtonElement>(null)
  useController({
    enabled: true,
    menu() {},
    switchPage() {},
    keyboard() {},
    search() {
      if (controllerScope() === document) probe.leaked++
    },
    play() {
      if (controllerScope() === document) probe.leaked++
    },
  })
  return (
    <div className="avalon-shell fullscreen" style={{ height: '100%', position: 'relative' }}>
      <main style={{ padding: 80 }}>
        <Origin />
        <button
          ref={trigger}
          onClick={() => {
            setNested(false)
            setMany(false)
            setOpen(true)
          }}
        >
          More actions
        </button>
        <button
          onClick={() => {
            setNested(false)
            setMany(true)
            setOpen(true)
          }}
        >
          Fourteen actions
        </button>
      </main>
      <AvalonActions
        open={open}
        title={nested ? 'Remove this game?' : 'More actions'}
        close={() => setOpen(false)}
        restoreFocus={() => trigger.current?.focus()}
      >
        {nested ? (
          <AvalonAction label="Keep game" onChoose={() => probe.nested++} />
        ) : many ? (
          labels.map((label, index) => (
            <AvalonAction key={label} label={label} disabled={index === 3} onChoose={() => probe.chosen++} />
          ))
        ) : (
          <>
            <AvalonAction label="Unavailable action" disabled onChoose={() => probe.disabled++} />
            <AvalonAction label="Pin this game" onChoose={() => probe.chosen++} />
            <AvalonAction
              label="Remove game"
              onChoose={() => {
                setNested(true)
                setOpen(true)
              }}
            />
          </>
        )}
      </AvalonActions>
    </div>
  )
}
document.documentElement.style.setProperty('--font-body', 'Avalon Body')
createRoot(document.getElementById('root')!).render(<Fixture />)

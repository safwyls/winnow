import { createRoot } from 'react-dom/client'
import { useState } from 'react'
import { AvalonAction, AvalonActions } from '../../src/renderer/themes/avalon-actions'

// The original test supplies two dynamic actions to a real retained settings shell.
// Keep that data fixture here while using the production panel and shell controller.
const origin = document.activeElement as HTMLElement
const host = document.createElement('div')
document.body.append(host)
function DynamicActions() {
  const [open, setOpen] = useState(true)
  return (
    <AvalonActions
      open={open}
      title="Choose an action"
      close={() => setOpen(false)}
      restoreFocus={() => origin.focus({ preventScroll: true })}
    >
      <AvalonAction
        label="Unavailable"
        disabled
        onChoose={() => {
          throw Error('Disabled action ran')
        }}
      />
      <AvalonAction label="Cancel" onChoose={() => {}} />
    </AvalonActions>
  )
}
createRoot(host).render(<DynamicActions />)

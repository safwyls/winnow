import { useEffect, useState } from 'react'
import type { ThemePage } from '../../shared/theme'
import { selectedStandardController } from '../../shared/controller-status'
import dpad from '../features/assets/xbox_dpad.svg?raw'
import accept from '../features/assets/xbox_button_a_outline.svg?raw'
import back from '../features/assets/xbox_button_b_outline.svg?raw'
import context from '../features/assets/xbox_button_y_outline.svg?raw'
import previous from '../features/assets/xbox_lt_outline.svg?raw'
import next from '../features/assets/xbox_rt_outline.svg?raw'
import './fullscreen-hints.css'

const connected = () => Boolean(selectedStandardController(Array.from(navigator.getGamepads?.() ?? [])))
function useControllerConnected() {
  const [value, setValue] = useState(connected)
  useEffect(() => {
    const update = () => setValue(connected())
    // Polling also sees controllers the browser exposes only after their first input.
    const timer = window.setInterval(update, 1000)
    for (const event of ['gamepadconnected', 'gamepaddisconnected', 'focus'])
      window.addEventListener(event, update)
    document.addEventListener('visibilitychange', update)
    return () => {
      clearInterval(timer)
      for (const event of ['gamepadconnected', 'gamepaddisconnected', 'focus'])
        window.removeEventListener(event, update)
      document.removeEventListener('visibilitychange', update)
    }
  }, [])
  return value
}

function Hint({ artwork, button, children }: { artwork: string; button: string; children: string }) {
  return (
    <span className="fullscreen-input-hint">
      <span
        aria-hidden="true"
        data-input-glyph={button}
        dangerouslySetInnerHTML={{ __html: artwork.replace('<svg ', '<svg viewBox="8 8 48 48" ') }}
      />
      <span className="sr-only">{button} · </span>
      {children}
    </span>
  )
}

export function FullscreenHints({ page }: { page: ThemePage }) {
  const controller = useControllerConnected()
  const contextual = page === 'library' ? 'Library options' : page === 'discover' ? 'More' : null
  return (
    <>
      <div
        className="fullscreen-input-hints"
        role="group"
        aria-label={controller ? 'Controller guidance' : 'Keyboard guidance'}
      >
        {controller ? (
          <>
            <Hint artwork={dpad} button="D-pad">
              Browse
            </Hint>
            <Hint artwork={accept} button="A">
              Select
            </Hint>
            {contextual && (
              <Hint artwork={context} button="Y">
                {contextual}
              </Hint>
            )}
            <Hint artwork={back} button="B">
              Back
            </Hint>
          </>
        ) : (
          <span>Arrows to browse · Enter to select · Esc to go back</span>
        )}
      </div>
      <div className="fullscreen-input-hints">
        {page === 'discover' ? (
          controller ? (
            <>
              <Hint artwork={previous} button="LT">
                Previous shelf
              </Hint>
              <Hint artwork={next} button="RT">
                Next shelf
              </Hint>
            </>
          ) : (
            <span>F11 · Back to desktop</span>
          )
        ) : (
          <span>F11 · Back to desktop</span>
        )}
      </div>
    </>
  )
}

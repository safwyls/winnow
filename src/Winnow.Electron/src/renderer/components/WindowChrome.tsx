import { useLayoutEffect, useState } from 'react'
import './window-chrome.css'

/** Native buttons sit over this themed drag region; OS movement and window commands stay native. */
export function WindowChrome({ fullscreen }: { fullscreen: boolean }) {
  const [supported, setSupported] = useState(!!window.winnow.windowChrome)
  useLayoutEffect(() => {
    if (!window.winnow.windowChrome) return
    const root = document.documentElement
    let disposed = false
    let lastText = ''
    const update = () => {
      const text = root.style.getPropertyValue('--text').trim() || '#f1ece4'
      if (text === lastText) return
      lastText = text
      void window.winnow.windowChrome!({ text })
        .then((value) => {
          if (!disposed) setSupported(value)
        })
        .catch(() => {})
    }
    update()
    const changes = new MutationObserver(update)
    changes.observe(root, { attributes: true, attributeFilter: ['style'] })
    return () => {
      disposed = true
      changes.disconnect()
    }
  }, [])
  useLayoutEffect(() => {
    const root = document.documentElement
    root.dataset.windowChrome = supported && !fullscreen ? 'themed' : 'none'
    return () => {
      delete root.dataset.windowChrome
    }
  }, [supported, fullscreen])
  if (!supported || fullscreen) return null
  return (
    <div className="window-chrome" role="banner" aria-label="Window title bar">
      <span>Winnow</span>
    </div>
  )
}

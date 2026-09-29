/** A retained opener can remain disabled while its data refreshes behind a closing dialog. */
export function restoreFocusWhenReady(target: HTMLElement | null) {
  if (!target?.isConnected) return
  let frame = 0
  const stop = () => {
    observer.disconnect()
    cancelAnimationFrame(frame)
    document.removeEventListener('pointerdown', stop, true)
    document.removeEventListener('keydown', stop, true)
  }
  const restore = () => {
    if (!target.isConnected) {
      stop()
      return
    }
    if (target.matches(':disabled') || target.closest('[inert], [hidden], [aria-hidden="true"]')) return
    target.focus({ preventScroll: true })
    if (document.activeElement === target) stop()
  }
  const observer = new MutationObserver(restore)
  observer.observe(document.body, {
    subtree: true,
    childList: true,
    attributes: true,
    attributeFilter: ['disabled', 'inert', 'hidden', 'aria-hidden'],
  })
  // A subsequent user action owns focus; a late refresh must not take it back.
  document.addEventListener('pointerdown', stop, true)
  document.addEventListener('keydown', stop, true)
  frame = requestAnimationFrame(restore)
}

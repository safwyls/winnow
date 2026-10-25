/** Keep the main event loop alive until cancellation and owned update work have settled. */
export function quitDrain(stop: () => Promise<void>, quit: () => void) {
  let drained = false,
    pending: Promise<void> | undefined
  return (event: { preventDefault(): void }): boolean => {
    if (drained) return false
    event.preventDefault()
    pending ??= stop().finally(() => {
      drained = true
      quit()
    })
    return true
  }
}

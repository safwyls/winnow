interface Delivery {
  once(event: 'show' | 'failed' | 'close', listener: () => void): unknown
  off(event: 'show' | 'failed' | 'close', listener: () => void): unknown
  show(): void
  close(): void
}
/** Native notification submission can fail asynchronously when OS notifications are disabled. */
export function deliverNotification(notification: Delivery, timeoutMs = 5000): Promise<boolean> {
  return new Promise((resolve) => {
    let settled = false
    const finish = (shown: boolean) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      notification.off('show', shownEvent)
      notification.off('failed', failure)
      notification.off('close', failure)
      if (!shown) notification.close()
      resolve(shown)
    }
    const shownEvent = () => finish(true)
    const failure = () => finish(false)
    const timer = setTimeout(failure, timeoutMs)
    notification.once('show', shownEvent)
    notification.once('failed', failure)
    notification.once('close', failure)
    try {
      notification.show()
    } catch {
      failure()
    }
  })
}

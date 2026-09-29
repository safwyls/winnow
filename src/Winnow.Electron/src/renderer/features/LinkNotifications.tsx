import { useEffect, useState } from 'react'
import type { LinkOpenResult } from '../../shared/bridge'
import { linkNoticeEvent } from '../api/client'
import './link-notifications.css'

export function LinkNotifications() {
  const [notice, setNotice] = useState<LinkOpenResult | null>(null)
  useEffect(() => {
    const receive = (event: Event) => {
      const value = (event as CustomEvent<LinkOpenResult>).detail
      if (value && typeof value.opened === 'boolean')
        setNotice(
          value.opened && !value.message
            ? null
            : {
                opened: value.opened,
                message:
                  typeof value.message === 'string' ? value.message : 'Could not open this link. Try again.',
              },
        )
    }
    window.addEventListener(linkNoticeEvent, receive)
    return () => window.removeEventListener(linkNoticeEvent, receive)
  }, [])
  if (!notice) return null
  return (
    <aside className="link-notification" aria-label="Link status">
      <p role={notice.opened ? 'status' : 'alert'}>{notice.message}</p>
      <button aria-label="Dismiss link status" onClick={() => setNotice(null)}>
        Dismiss
      </button>
    </aside>
  )
}

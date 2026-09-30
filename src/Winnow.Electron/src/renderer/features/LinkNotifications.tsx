import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { LinkOpenResult } from '../../shared/bridge'
import { linkNoticeEvent } from '../api/client'
import './link-notifications.css'

function noticeHost() {
  const available = (element: Element) =>
    !element.closest('[aria-hidden="true"],[hidden],[inert]') &&
    element.getAttribute('data-state') !== 'closed'
  const focused = document.activeElement?.closest<HTMLElement>('[role="dialog"],[role="alertdialog"]')
  if (focused && available(focused)) return focused
  return (
    [...document.querySelectorAll<HTMLElement>('[role="dialog"],[role="alertdialog"]')]
      .filter(available)
      .at(-1) ?? document.body
  )
}

export function LinkNotifications() {
  const [notice, setNotice] = useState<LinkOpenResult | null>(null)
  const [portal, setPortal] = useState<HTMLElement>(document.body)
  const element = useRef<HTMLElement>(null)
  const origin = useRef<HTMLElement | null>(null)
  useEffect(() => {
    const receive = (event: Event) => {
      const value = (event as CustomEvent<LinkOpenResult>).detail
      if (value && typeof value.opened === 'boolean') {
        origin.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
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
    }
    window.addEventListener(linkNoticeEvent, receive)
    return () => window.removeEventListener(linkNoticeEvent, receive)
  }, [])
  useLayoutEffect(() => {
    if (!notice) return
    const follow = () => setPortal(noticeHost())
    follow()
    const observer = new MutationObserver(follow)
    observer.observe(document.body, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['aria-hidden', 'hidden', 'inert', 'data-state'],
    })
    return () => observer.disconnect()
  }, [notice])
  useLayoutEffect(() => {
    const node = element.current
    if (!notice || !node?.isConnected) return
    // The top layer avoids modal clipping; the portal keeps keyboard/controller focus in its owner.
    if (node.showPopover) {
      if (!node.matches(':popover-open')) node.showPopover()
    } else node.style.display = 'flex'
  }, [notice, portal])
  useLayoutEffect(() => {
    if (!notice) return
    const key = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || !element.current?.contains(event.target as Node)) return
      // Dialogs consume Escape during document capture, before React's bubble handlers.
      event.preventDefault()
      event.stopPropagation()
      dismiss()
    }
    window.addEventListener('keydown', key, true)
    return () => window.removeEventListener('keydown', key, true)
  }, [notice])
  function dismiss() {
    setNotice(null)
    if (origin.current?.isConnected) origin.current.focus({ preventScroll: true })
  }
  if (!notice) return null
  return createPortal(
    <aside
      ref={element}
      popover="manual"
      className="link-notification"
      aria-label="Link status"
      onPointerDown={(event) => event.stopPropagation()}
    >
      <p role={notice.opened ? 'status' : 'alert'}>{notice.message}</p>
      <button aria-label="Dismiss link status" onClick={dismiss}>
        Dismiss
      </button>
    </aside>,
    portal,
  )
}

import type { KeyboardEvent as ReactKeyboardEvent } from 'react'

const dialogSelector = '[role="dialog"], [role="alertdialog"]'

/** The innermost dialog around an event target; null when focus sits outside every dialog. */
export function escapeOwner(target: EventTarget | null): Element | null {
  return target instanceof Element ? target.closest(dialogSelector) : null
}

/**
 * Radix moves its document Escape listener to the highest layer in a deferred render, so an
 * Escape pressed right after a nested dialog opens or closes can reach a stale parent layer,
 * which swallows it, or no layer at all. A dialog's own keydown handler calls this to answer
 * an Escape whose target it owns; Radix's handler stops immediate propagation when it has
 * already answered, so the two never both act.
 */
export function isOwnEscape(event: ReactKeyboardEvent<HTMLElement>): boolean {
  return event.key === 'Escape' && escapeOwner(event.target) === event.currentTarget
}

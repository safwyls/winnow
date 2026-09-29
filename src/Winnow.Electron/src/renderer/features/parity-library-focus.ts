import { useLayoutEffect, useRef } from 'react'

/** Inline editors return keyboard/controller users to the action that opened them. */
export function useInlineEditorFocus(open: boolean) {
  const trigger = useRef<HTMLButtonElement>(null)
  const editor = useRef<HTMLFormElement>(null)
  const previous = useRef(false)
  useLayoutEffect(() => {
    if (open)
      editor.current
        ?.querySelector<HTMLElement>(
          'input:not(:disabled), textarea:not(:disabled), select:not(:disabled), button:not(:disabled)',
        )
        ?.focus({ preventScroll: true })
    else if (previous.current) trigger.current?.focus({ preventScroll: true })
    previous.current = open
  }, [open])
  return { trigger, editor }
}

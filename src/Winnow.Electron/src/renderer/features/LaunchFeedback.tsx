import { createContext, useContext, useEffect, useState, useSyncExternalStore } from 'react'
import { LaunchFeedback } from './launch-feedback'
import type { Mode } from '../api/types'
import './launch-feedback.css'

export const LaunchFeedbackContext = createContext<LaunchFeedback | null>(null)
export const useLaunchFeedback = () => useContext(LaunchFeedbackContext)
export function useLaunchFeedbackHost() {
  const [feedback] = useState(() => new LaunchFeedback())
  useEffect(() => {
    feedback.start()
    const detach = window.winnow.onEvent(feedback.observe)
    return () => {
      detach()
      feedback.dispose()
    }
  }, [feedback])
  return feedback
}
export function LaunchFeedbackStrip({ feedback, mode }: { feedback: LaunchFeedback; mode: Mode }) {
  const state = useSyncExternalStore(feedback.subscribe, feedback.getSnapshot)
  if (!state.open) return null
  return (
    <aside
      className={`launch-feedback ${mode}`}
      role="status"
      aria-live="polite"
      aria-atomic="true"
      data-waiting={state.waiting}
      data-problem={state.problem}
    >
      {mode === 'desktop' && <span className="launch-feedback-dot" aria-hidden="true" />}
      <span className="launch-feedback-message">{state.message}</span>
    </aside>
  )
}

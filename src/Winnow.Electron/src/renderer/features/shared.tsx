import type { ReactNode } from 'react'
import { ApiError, errorMessage } from '../api/client'

export function Notice({ error, message }: { error?: unknown; message?: string | null }) {
  if (error)
    return (
      <p className="error-message" role="alert">
        {errorMessage(error)}
        {error instanceof ApiError && error.uncertain
          ? ' The response was interrupted. Check the refreshed saved state before trying the action again.'
          : ''}
      </p>
    )
  return message ? (
    <p className="status-message" role="status">
      {message}
    </p>
  ) : null
}
export function Empty({ children }: { children: ReactNode }) {
  return <div className="empty-state">{children}</div>
}

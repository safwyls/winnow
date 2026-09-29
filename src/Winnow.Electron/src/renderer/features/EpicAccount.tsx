import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import type { EpicSignInPreparation, EpicSignInResult } from '../../shared/epic'
import { useSetupBusy } from './settingsState'
import { Notice } from './shared'

export function epicResultMessage(result: EpicSignInResult): string {
  if (result.succeeded)
    return `${result.displayName ? `Epic Games connected as ${result.displayName}.` : 'Epic Games connected. Epic did not provide a display name.'}${result.persisted ? '' : ' This session could not be saved securely and lasts for this run only.'}`
  switch (result.failure) {
    case 1:
      return 'No Epic OAuth client credentials are available.'
    case 2:
      return 'Epic rejected the OAuth client credentials. Try again after the launcher credentials have been updated.'
    case 3:
      return 'Epic rejected the code. Codes are single-use and expire within minutes. Start a fresh sign-in.'
    case 4:
      return 'Could not finish connecting to Epic. Check the current connection before trying again.'
    case 6:
      return 'Sign-in cancelled. Nothing was changed.'
    case 7:
      return 'The Epic sign-in window could not open. You can continue in your own browser.'
    case 8:
      return 'Epic finished without handing back a usable code. You can continue in your own browser. Your library is unchanged.'
    case 9:
      return 'The window closed without an Epic account being signed in. Complete the sign-in on Epic’s page, or continue in your own browser.'
    default:
      return 'Epic answered with something Winnow did not understand. Start a fresh sign-in.'
  }
}

export function NativeEpicAccount({
  label = 'Connect Epic Games',
  showAction = true,
  onBusyChange,
}: {
  label?: string
  showAction?: boolean
  onBusyChange?: (busy: boolean) => void
}) {
  const [preparation, setPreparation] = useState<EpicSignInPreparation | null>(null)
  const [accepted, setAccepted] = useState(false)
  const [manual, setManual] = useState(false)
  const [callback, setCallback] = useState('')
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState<unknown>(null)
  const lifetime = useRef(0),
    ownsAttempt = useRef(false),
    active = useRef(false)
  const client = useQueryClient()
  const wasHidden = useRef(!showAction)
  useEffect(() => {
    if (showAction && wasHidden.current) {
      setMessage('')
      setError(null)
    }
    wasHidden.current = !showAction
  }, [showAction])
  useSetupBusy(pending || !!preparation)
  useEffect(() => {
    onBusyChange?.(pending || !!preparation)
    return () => onBusyChange?.(false)
  }, [pending, preparation, onBusyChange])
  useEffect(
    () => () => {
      lifetime.current++
      if (ownsAttempt.current) void window.winnow.cancelEpicSignIn?.().catch(() => {})
    },
    [],
  )
  async function refresh() {
    await client.cancelQueries({ queryKey: ['api', 'connections.get'] })
    await client.invalidateQueries({ queryKey: ['api'] })
  }
  async function begin() {
    if (active.current) return
    active.current = true
    const generation = lifetime.current
    ownsAttempt.current = true
    setPending(true)
    setError(null)
    setMessage('')
    setAccepted(false)
    setManual(false)
    setCallback('')
    try {
      const next = await window.winnow.prepareEpicSignIn!()
      if (generation !== lifetime.current) return
      ownsAttempt.current = !!next
      setPreparation(next)
      if (!next) setMessage('Sign-in cancelled. Nothing was changed.')
    } catch (failure) {
      ownsAttempt.current = false
      if (generation === lifetime.current) setError(failure)
    } finally {
      if (generation === lifetime.current) {
        active.current = false
        setPending(false)
      }
    }
  }
  async function cancel() {
    try {
      if ((await window.winnow.cancelEpicSignIn!()) || !active.current) {
        lifetime.current++
        active.current = false
        ownsAttempt.current = false
        setPreparation(null)
        setCallback('')
        setPending(false)
        setError(null)
        setMessage('Sign-in cancelled. Nothing was changed.')
      }
    } catch {
      setError(new Error('The Epic window could not close. Close it and try again.'))
    }
  }
  async function run(action: 'embedded' | 'browser' | 'complete') {
    if (!preparation || !accepted || active.current) return
    const generation = lifetime.current,
      options = { attemptId: preparation.attemptId, consentGranted: true }
    active.current = true
    setPending(true)
    setError(null)
    setMessage('')
    try {
      if (action === 'browser') {
        await window.winnow.openEpicSignInInBrowser!(options)
        if (generation === lifetime.current) setManual(true)
      } else {
        const result =
          action === 'embedded'
            ? await window.winnow.epicSignIn!(options)
            : await window.winnow.completeEpicSignIn!({ ...options, callback })
        if (generation !== lifetime.current) return
        setCallback('')
        setMessage(epicResultMessage(result))
        if (result.canRetryManually) setManual(true)
        else {
          ownsAttempt.current = false
          setPreparation(null)
        }
        await refresh()
      }
    } catch (failure) {
      if (generation === lifetime.current) setError(failure)
    } finally {
      if (generation === lifetime.current) {
        active.current = false
        setPending(false)
      }
    }
  }
  return (
    <div
      className="epic-account"
      onKeyDownCapture={(event) => {
        if (event.key === 'Escape' && (pending || preparation)) {
          event.preventDefault()
          event.stopPropagation()
          void cancel()
        }
      }}
    >
      {preparation ? (
        <div className="editor-form" aria-busy={pending}>
          <p>{preparation.consentNotice}</p>
          <label className="check-field">
            <input
              type="checkbox"
              checked={accepted}
              disabled={pending}
              onChange={(event) => setAccepted(event.target.checked)}
            />
            I agree to connect this account
          </label>
          <div className="form-actions">
            {!manual && (
              <button disabled={!accepted || pending} onClick={() => void run('embedded')}>
                Open Epic sign-in window
              </button>
            )}
            <button disabled={!accepted || pending} onClick={() => void run('browser')}>
              Continue in your browser
            </button>
          </div>
          {manual && (
            <>
              <p className="muted">
                After signing in, copy the complete final address from your browser and paste it below. It may
                show an unavailable page; the address contains the response.
              </p>
              <label className="field">
                Final sign-in address
                <input
                  type="password"
                  autoComplete="off"
                  maxLength={16384}
                  disabled={pending}
                  value={callback}
                  onChange={(event) => setCallback(event.target.value)}
                />
              </label>
              <button
                disabled={!accepted || pending || !callback.trim()}
                onClick={() => void run('complete')}
              >
                Finish connecting
              </button>
            </>
          )}
          {pending && (
            <p role="status">Waiting for Epic sign-in. You can cancel before the account is saved.</p>
          )}
          <button onClick={() => void cancel()}>Cancel Epic sign-in</button>
        </div>
      ) : (
        <div className="form-actions">
          {showAction && (
            <button disabled={pending} onClick={() => void begin()}>
              {pending ? 'Preparing Epic sign-in…' : label}
            </button>
          )}
          {pending && <button onClick={() => void cancel()}>Cancel Epic sign-in</button>}
        </div>
      )}
      <Notice error={error} message={message} />
    </div>
  )
}

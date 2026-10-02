import { useEffect, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { createClientId, openExternal, request } from '../api/client'
import type { PluginSnapshot } from '../api/types'
import { validPluginChallenge, type PluginChallenge } from './plugin-policy'
function delay(milliseconds: number, signal: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    signal.throwIfAborted()
    const abort = () => {
      clearTimeout(timer)
      reject(signal.reason)
    }
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', abort)
      resolve()
    }, milliseconds)
    signal.addEventListener('abort', abort, { once: true })
  })
}

export function PluginAccount({ plugin, busy }: { plugin: PluginSnapshot; busy: boolean }) {
  const [challenge, setChallenge] = useState<PluginChallenge | null>(null)
  const [message, setMessage] = useState('')
  const lifetime = useRef<AbortController | null>(null)
  const mounted = useRef(true)
  const clientId = useRef(createClientId()).current
  const client = useQueryClient()
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      lifetime.current?.abort()
    }
  }, [])
  const action = useMutation({
    mutationKey: ['plugin-write', plugin.id],
    retry: false,
    mutationFn: async (kind: 'in' | 'out') => {
      if (lifetime.current) return
      const controller = new AbortController()
      lifetime.current = controller
      const signal = controller.signal
      let attempt: PluginChallenge | null = null,
        connected = false
      let expiryTimer: ReturnType<typeof setTimeout> | undefined
      const status = (text: string) => {
        if (mounted.current) setMessage(text)
      }
      try {
        if (kind === 'out') {
          await request('plugins.signOut', { pluginId: plugin.id })
          status('Signed out. Imported games remain in your library.')
          await client.invalidateQueries({ queryKey: ['api'] })
          return
        }
        status('Preparing sign-in…')
        // Keep the response even after departure so its newly issued attempt can be canceled.
        const result = await request<{ challenge: PluginChallenge | null }>(
          'plugins.signIn',
          { pluginId: plugin.id },
          { clientId },
        )
        attempt = result.challenge
        signal.throwIfAborted()
        if (!validPluginChallenge(attempt, plugin.accountHosts ?? [])) {
          status('Could not start sign-in. Check the saved settings and try again.')
          return
        }
        setChallenge(attempt)
        status('Open the sign-in page and enter this code.')
        expiryTimer = setTimeout(
          () => {
            if (mounted.current) setChallenge(null)
            status('The sign-in code expired. Start sign-in again for a new code.')
            controller.abort('expired')
          },
          Date.parse(attempt.expiresAt) - Date.now(),
        )
        let interval = Math.min(300, attempt.pollIntervalSeconds)
        while (!signal.aborted) {
          const remaining = Date.parse(attempt.expiresAt) - Date.now()
          if (remaining <= 0) {
            status('The sign-in code expired. Start sign-in again for a new code.')
            return
          }
          await delay(Math.min(interval * 1000, remaining), signal)
          if (Date.parse(attempt.expiresAt) <= Date.now()) continue
          const result = await request<{ state: number }>(
            'plugins.poll',
            { pluginId: plugin.id },
            { clientId, attemptId: attempt.attemptId },
            signal,
          )
          signal.throwIfAborted()
          if (result.state === 2) {
            connected = true
            status('Signed in. Refresh queued.')
            await client.invalidateQueries({ queryKey: ['api'] })
            return
          }
          if (result.state === 1) interval = Math.min(interval + 5, 300)
          else if (result.state !== 0) {
            status('Sign-in was not completed. Try again when you are ready.')
            return
          }
        }
      } catch {
        status(
          signal.reason === 'expired'
            ? 'The sign-in code expired. Start sign-in again for a new code.'
            : signal.aborted
              ? 'Sign-in cancelled.'
              : kind === 'out'
                ? 'Could not sign out. Try again.'
                : 'Could not complete sign-in. Check the saved settings and try again.',
        )
      } finally {
        clearTimeout(expiryTimer)
        if (mounted.current) setChallenge(null)
        if (attempt && !connected)
          await request(
            'plugins.cancel',
            { pluginId: plugin.id },
            { clientId, attemptId: attempt.attemptId },
          ).catch(() => {})
        lifetime.current = null
      }
    },
  })
  const enabled = plugin.canConfigure && plugin.enabled && plugin.isLoaded && !busy
  return (
    <section className="plugin-account" aria-label={`${plugin.name} account`}>
      {plugin.accountConnected ? (
        <>
          <p>Account connected</p>
          <button
            disabled={!enabled}
            aria-label={`Sign out of ${plugin.name}`}
            onClick={() => action.mutate('out')}
          >
            Sign out
          </button>
        </>
      ) : action.isPending ? (
        <>
          {challenge && (
            <div className="conflict-panel">
              <p>Enter this code on the provider's sign-in page:</p>
              <strong
                className="device-code"
                role="status"
                aria-label={`Sign-in code: ${challenge.userCode}`}
              >
                {challenge.userCode}
              </strong>
              <p className="plugin-verification-url">{challenge.verificationUrl}</p>
              <button
                aria-label={`Open ${plugin.name} sign-in page`}
                onClick={() => void openExternal(challenge.verificationUrl)}
              >
                Open sign-in page
              </button>
            </div>
          )}
          <button
            aria-label={`Cancel ${plugin.name} sign-in`}
            onClick={() => {
              lifetime.current?.abort()
              setChallenge(null)
            }}
          >
            Cancel sign-in
          </button>
        </>
      ) : (
        <button
          disabled={!enabled}
          aria-label={`Sign in to ${plugin.name}`}
          onClick={() => action.mutate('in')}
        >
          Connect account
        </button>
      )}
      <p role="status">{message || (plugin.accountConnected ? 'Signed in.' : 'Not signed in.')}</p>
    </section>
  )
}

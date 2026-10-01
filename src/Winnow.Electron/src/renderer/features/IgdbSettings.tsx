import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { openExternal } from '../api/client'
import { useApiQuery, useCommand } from '../api/hooks'
import type { IgdbConnection, Mode } from '../api/types'
import { useSetupBusy } from './settingsState'
import { Notice } from './shared'
import './igdb-settings.css'
import { useFullscreenSettingsEntry } from './FullscreenSettingRows'

const fallback =
  ' Credentials from environment variables or configuration will be used when no saved pair is available.'
export const igdbSavedStatus = (snapshot: IgdbConnection) =>
  snapshot.isReadable
    ? 'Credentials are saved on this device. Enter both fields to replace them.'
    : snapshot.hasSavedCredentials
      ? 'Saved credentials need to be re-entered. Enter your client ID and secret, then save.' +
        (snapshot.hasConfigurationCredentials ? fallback : '')
      : snapshot.hasConfigurationCredentials
        ? 'IGDB credentials are supplied by environment variables or configuration. A saved pair takes priority.'
        : 'Add your Twitch application credentials to fetch IGDB game details.'

export function IgdbConnectionPanel({
  mode = 'desktop',
  sectioned = false,
  bounded = false,
}: {
  mode?: Mode
  sectioned?: boolean
  bounded?: boolean
}) {
  const query = useApiQuery<IgdbConnection>('connections.igdb.get')
  const entry = useFullscreenSettingsEntry(
    sectioned && (!!query.data || query.isError),
    query.isError ? 'retry' : 'credentials',
  )
  return (
    <section ref={entry} className="igdb-connection-panel">
      {query.error && (
        <>
          <Notice error={new Error('Could not read IGDB settings. Try loading them again.')} />
          <button
            disabled={query.isFetching}
            onClick={() => {
              void query.refetch()
            }}
          >
            Retry IGDB settings
          </button>
        </>
      )}
      {!query.data && !query.error && <p role="status">Loading IGDB settings…</p>}
      {query.data && (
        <IgdbForm snapshot={query.data} mode={mode} sectioned={sectioned} bounded={bounded} key={mode} />
      )}
    </section>
  )
}

export function IgdbForm({
  snapshot,
  mode = 'desktop',
  sectioned = false,
  bounded = false,
}: {
  snapshot: IgdbConnection
  mode?: Mode
  sectioned?: boolean
  bounded?: boolean
}) {
  const [clientId, setClientId] = useState(snapshot.clientId)
  const [secret, setSecret] = useState('')
  const [message, setMessage] = useState('')
  const [failure, setFailure] = useState('')
  const command = useCommand<number | boolean>()
  const working = useRef(false)
  const acknowledged = useRef(snapshot.clientId)
  const restoreFocus = useRef(false)
  const saveButton = useRef<HTMLButtonElement>(null)
  const idInput = useRef<HTMLInputElement>(null)
  const secretInput = useRef<HTMLInputElement>(null)
  const feedback = useRef<HTMLDivElement>(null)
  const id = useId()
  useSetupBusy(command.isPending)
  useEffect(() => {
    const previous = acknowledged.current
    acknowledged.current = snapshot.clientId
    setClientId((current) => (current === previous ? snapshot.clientId : current))
  }, [snapshot.clientId])
  useLayoutEffect(() => {
    if (command.isPending) {
      const cancel = () => {
        restoreFocus.current = false
      }
      document.addEventListener('pointerdown', cancel, true)
      document.addEventListener('keydown', cancel, true)
      return () => {
        document.removeEventListener('pointerdown', cancel, true)
        document.removeEventListener('keydown', cancel, true)
      }
    }
    if (restoreFocus.current && document.activeElement === document.body)
      saveButton.current?.focus({ preventScroll: true })
    restoreFocus.current = false
  }, [command.isPending])
  useLayoutEffect(() => {
    if ((message || failure) && feedback.current?.closest('form')?.contains(document.activeElement))
      feedback.current.scrollIntoView?.({ block: 'nearest', behavior: 'instant' })
  }, [message, failure, command.isPending])
  async function change(remove: boolean) {
    if (working.current) return
    setFailure('')
    setMessage('')
    if (!remove && (!clientId.trim() || !secret.trim())) {
      setFailure('Enter both your client ID and client secret before saving.')
      ;(!clientId.trim() ? idInput : secretInput).current?.focus()
      return
    }
    working.current = true
    restoreFocus.current = !!document.activeElement?.closest('.igdb-settings-form')
    try {
      const result = await command.mutateAsync(
        remove
          ? { route: 'connections.igdb.delete' }
          : { route: 'connections.igdb.put', body: { clientId: clientId.trim(), clientSecret: secret } },
      )
      if (!remove && result === 1) {
        setFailure('Enter both your client ID and client secret before saving.')
        return
      }
      if (!remove && result === 2) {
        setFailure(
          'This device could not protect the secret, so nothing was saved. Use Igdb__ClientId and Igdb__ClientSecret environment variables instead.',
        )
        return
      }
      if (remove ? typeof result !== 'boolean' : result !== 0)
        throw new Error('Unexpected credential response')
      setClientId(remove ? '' : clientId.trim())
      setSecret('')
      setMessage(
        remove
          ? 'Saved credentials removed. The change is active now.' + (result ? fallback : '')
          : 'Credentials saved. Metadata refresh queued. IGDB will check them when fetching details.',
      )
    } catch {
      // Service errors must never echo the entered secret or storage exception details.
      setFailure(
        `Could not ${remove ? 'remove' : 'save'} IGDB credentials. Check the refreshed saved state and that Winnow's data folder is writable, then try again.`,
      )
    } finally {
      working.current = false
    }
  }
  async function setup() {
    setFailure('')
    try {
      await openExternal('https://dev.twitch.tv/console/apps', { failure: 'inline' })
    } catch {
      setFailure('Could not open the page. Visit dev.twitch.tv/console/apps to create a Twitch application.')
    }
  }
  const fields = (
    <>
      <p>
        IGDB adds game details and artwork. Enter the client ID and secret from your Twitch developer
        application.
      </p>
      <button
        type="button"
        onClick={() => {
          void setup()
        }}
      >
        Get IGDB credentials
      </button>
      {sectioned && (
        <>
          <hr className="fullscreen-information-rule" />
          <h2 className="fullscreen-information-heading">Credentials</h2>
        </>
      )}
      <label className="field" htmlFor={`${id}-client`}>
        Client ID
        <input
          ref={idInput}
          id={`${id}-client`}
          autoComplete="off"
          value={clientId}
          onChange={(event) => setClientId(event.target.value)}
          disabled={command.isPending}
          required
          maxLength={512}
        />
      </label>
      <label className="field" htmlFor={`${id}-secret`}>
        Client secret
        <input
          ref={secretInput}
          id={`${id}-secret`}
          type="password"
          autoComplete="off"
          value={secret}
          placeholder="Enter a secret to save or replace credentials"
          onChange={(event) => setSecret(event.target.value)}
          disabled={command.isPending}
          required
          maxLength={4096}
        />
      </label>
      <p className="igdb-storage-help">
        The secret is stored securely on this device. Changes take effect immediately.
      </p>
      {sectioned && <hr className="fullscreen-information-rule" />}
    </>
  )
  return (
    <form
      className={`igdb-settings-form mode-${mode}${bounded ? ' igdb-settings-bounded' : ''}`}
      aria-label="IGDB credentials"
      noValidate
      onSubmit={(event) => {
        event.preventDefault()
        void change(false)
      }}
    >
      {bounded ? <div className="igdb-settings-fields">{fields}</div> : fields}
      <div className="igdb-settings-actions">
        <div className="form-actions">
          <button ref={saveButton} disabled={command.isPending}>
            Save credentials
          </button>
          <button
            type="button"
            disabled={command.isPending}
            onClick={() => {
              void change(true)
            }}
          >
            Remove saved credentials
          </button>
        </div>
        <div ref={feedback}>
          <Notice
            error={failure ? new Error(failure) : undefined}
            message={message || igdbSavedStatus(snapshot)}
          />
        </div>
      </div>
    </form>
  )
}

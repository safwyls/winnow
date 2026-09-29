import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import * as Dialog from '@radix-ui/react-dialog'
import { request } from '../api/client'
import { useApiQuery, useCommand } from '../api/hooks'
import type { IgdbConnection, LibraryPreferences, Mode, StoreConnections } from '../api/types'
import { EpicAccount, IgdbForm, LibraryPreferenceForm, SteamKeyForm, SteamAccount } from './Settings'
import { ApplicationPreferences, LibraryPresentationPreferences } from './SettingsPreferences'
import { SetupBusyContext, SetupErrorContext, useSetupBusy } from './settingsState'
import { Notice } from './shared'
import './setup.css'

export interface SetupProgress {
  step: number | null
  problem?: string | null
}
const steps = [
  [
    'Welcome to Winnow',
    'Bring your games together, choose a look, and make yourself comfortable. Every step is optional. You can change everything later in Settings.',
  ],
  [
    'Fill in the details',
    'IGDB adds game details and artwork. Save your credentials here, or skip this step and use the available store metadata.',
  ],
  [
    'Your Steam library',
    'Installed Steam games are discovered locally. Connecting Steam can add games you own but have never installed.',
  ],
  [
    'Your Epic library',
    'Installed Epic games are discovered locally. Sign in if you want Winnow to include the rest of your Epic library.',
  ],
  [
    'Your GOG library',
    'Winnow reads your local Galaxy library automatically. No GOG sign-in is needed or offered. If Galaxy is not installed, you can skip this step.',
  ],
  [
    'Make it yours',
    'Choose a theme and adjust the look. Changes apply immediately and are kept if you skip ahead.',
  ],
  [
    'How Winnow fits your desktop',
    'Choose startup and window behavior. Changes save as you make them; skipping keeps your current preferences.',
  ],
  [
    'Choose what appears',
    'Choose which games Winnow shows. Changes save as you make them, and you can adjust them later in Settings.',
  ],
  [
    'Your library is ready to explore',
    'Your saved choices are in place. Library discovery and metadata may still be running. Anything you skipped is available in Settings, where you can also reopen setup.',
  ],
] as const

export function Setup({
  mode,
  onComplete,
  appearance,
  onOpenChange,
}: {
  mode: Mode
  onComplete?: () => void
  appearance?: ReactNode
  onOpenChange?: (open: boolean) => void
}) {
  const progress = useApiQuery<SetupProgress>('setup.get')
  const client = useQueryClient()
  const [failure, setFailure] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [busyEditors, setBusyEditors] = useState<Set<string>>(() => new Set())
  const [failedPreferences, setFailedPreferences] = useState<Set<string>>(() => new Set())
  const savingRef = useRef(false)
  const nextButton = useRef<HTMLButtonElement>(null)
  const step = progress.error ? 0 : progress.data?.step
  const open = typeof step === 'number'
  const safeStep = typeof step === 'number' && step >= 0 && step < steps.length ? step : 0
  const busy = saving || busyEditors.size > 0
  const reportBusy = useCallback(
    (id: string, value: boolean) =>
      setBusyEditors((previous) => {
        if (previous.has(id) === value) return previous
        const next = new Set(previous)
        if (value) next.add(id)
        else next.delete(id)
        return next
      }),
    [],
  )
  const reportError = useCallback(
    (id: string, value: boolean) =>
      setFailedPreferences((previous) => {
        if (previous.has(id) === value) return previous
        const next = new Set(previous)
        if (value) next.add(id)
        else next.delete(id)
        return next
      }),
    [],
  )
  useEffect(() => {
    onOpenChange?.(open)
  }, [open, onOpenChange])
  useEffect(() => {
    if (open) nextButton.current?.focus()
  }, [safeStep, mode, open])
  async function move(cursor: number | null, continueStep = false) {
    if (savingRef.current || busyEditors.size) return
    if (continueStep && failedPreferences.size) {
      setFailure(
        'A preference could not be saved. Try changing it again, or skip this step to continue with your saved settings.',
      )
      return
    }
    savingRef.current = true
    setSaving(true)
    setFailure(null)
    try {
      await request('setup.put', undefined, { step: cursor })
      client.setQueryData(['api', 'setup.get', undefined], { step: cursor, problem: null })
      if (cursor === null) onComplete?.()
    } catch {
      setFailure(
        "Could not save setup progress. Check that Winnow's data folder is writable, then try again.",
      )
    } finally {
      savingRef.current = false
      setSaving(false)
    }
  }
  return (
    <Dialog.Root open={open} modal>
      <Dialog.Portal>
        <Dialog.Overlay className="setup-overlay" />
        <Dialog.Content
          className={`setup-dialog mode-${mode}`}
          aria-label="Winnow setup"
          onEscapeKeyDown={(event) => {
            event.preventDefault()
            // The TV back action returns through the wizard. Desktop Escape skips an optional step.
            if (busy) return
            if (mode === 'fullscreen' && safeStep > 0) void move(safeStep - 1)
            else if (safeStep > 0 && safeStep < 8) void move(safeStep + 1)
          }}
          onInteractOutside={(event) => event.preventDefault()}
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            nextButton.current?.focus()
          }}
        >
          <header className="setup-header">
            <p className="eyebrow">
              SETUP · {safeStep + 1} OF {steps.length}
            </p>
            <Dialog.Title>{steps[safeStep][0]}</Dialog.Title>
            <Dialog.Description>{steps[safeStep][1]}</Dialog.Description>
          </header>
          <div className="setup-body" key={`${mode}:${safeStep}`}>
            <SetupBusyContext.Provider value={reportBusy}>
              <SetupErrorContext.Provider value={reportError}>
                {safeStep === 0 && (
                  <p className="setup-welcome">Your games. One library. Something worth coming back to.</p>
                )}
                {safeStep === 1 && <SetupIgdb />}
                {safeStep === 2 && <SetupSteam />}
                {safeStep === 3 && <SetupEpic />}
                {safeStep === 4 && (
                  <p>Galaxy discovery runs in the background. Continue whenever you are ready.</p>
                )}
                {safeStep === 5 &&
                  (appearance ?? <p>Open Theme Studio after setup to choose your appearance.</p>)}
                {safeStep === 6 && <ApplicationPreferences setup />}
                {safeStep === 7 && <SetupLibrary />}
                {safeStep === 8 && <p>Anything you skipped remains available in Settings.</p>}
              </SetupErrorContext.Provider>
            </SetupBusyContext.Provider>
          </div>
          <footer className="setup-footer">
            <Notice
              message={
                failure ||
                progress.data?.problem ||
                (progress.error
                  ? 'Could not read setup progress. Continue to try again, or skip setup.'
                  : null)
              }
            />
            <div className="setup-actions">
              <button disabled={busy} onClick={() => void move(null)}>
                Skip setup
              </button>
              <div className="form-actions">
                {safeStep > 0 && (
                  <button disabled={busy} onClick={() => void move(safeStep - 1)}>
                    Back
                  </button>
                )}
                {safeStep > 0 && safeStep < 8 && (
                  <button disabled={busy} onClick={() => void move(safeStep + 1)}>
                    Skip this step
                  </button>
                )}
                <button
                  ref={nextButton}
                  className="primary"
                  disabled={busy}
                  onClick={() => void move(safeStep === 8 ? null : safeStep + 1, true)}
                >
                  {safeStep === 0 ? 'Get started' : safeStep === 8 ? 'Open my library' : 'Continue'}
                </button>
              </div>
            </div>
          </footer>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

function SetupIgdb() {
  const igdb = useApiQuery<IgdbConnection>('connections.igdb.get')
  return (
    <>
      <Notice error={igdb.error} />
      {igdb.data && <IgdbForm snapshot={igdb.data} />}
      <p className="muted">
        Saved IGDB credentials take effect immediately. Metadata fills in in the background. Continue does not
        save unsaved credentials.
      </p>
    </>
  )
}
function SetupSteam() {
  const stores = useApiQuery<StoreConnections>('connections.get')
  const command = useCommand()
  useSetupBusy(command.isPending)
  return (
    <>
      <SteamAccount />
      <SteamKeyForm />
      <Notice error={stores.error || command.error} />
      {stores.data?.steam.hasSession && (
        <button
          disabled={command.isPending}
          onClick={() => command.mutate({ route: 'connections.steam.signout' })}
        >
          Sign out of Steam
        </button>
      )}
    </>
  )
}
function SetupEpic() {
  const stores = useApiQuery<StoreConnections>('connections.get')
  const command = useCommand()
  useSetupBusy(command.isPending)
  return (
    <>
      <Notice error={stores.error || command.error} />
      {stores.data?.epic ? (
        <>
          <p>
            Epic Games is connected{stores.data.epic.displayName ? ` as ${stores.data.epic.displayName}` : ''}
            .
          </p>
          <button
            disabled={command.isPending}
            onClick={() => command.mutate({ route: 'connections.epic.signout' })}
          >
            Sign out of Epic
          </button>
        </>
      ) : (
        <EpicAccount />
      )}
    </>
  )
}
function SetupLibrary() {
  const preferences = useApiQuery<LibraryPreferences>('preferences.library.get')
  return (
    <>
      <Notice error={preferences.error} />
      {preferences.data && <LibraryPreferenceForm initial={preferences.data} />}
      <LibraryPresentationPreferences />
    </>
  )
}

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import * as Dialog from '@radix-ui/react-dialog'
import { request } from '../api/client'
import { useApiQuery } from '../api/hooks'
import type { IgdbConnection, LibraryPreferences, Mode, StoreConnections } from '../api/types'
import { EpicConnectionCard, LibraryPreferenceForm, SteamConnectionCard } from './Settings'
import { IgdbConnectionPanel, igdbSavedStatus } from './IgdbSettings'
import { RatingCapPreference } from './RatingCap'
import { ApplicationPreferences, LibraryPresentationPreferences } from './SettingsPreferences'
import { SetupBusyContext, SetupErrorContext } from './settingsState'
import { Notice } from './shared'
import { steamConnectionState } from './steamConnection'
import { AvalonAmbientBackdrop } from '../themes/avalon-ambient-backdrop'
import { editable } from '../controller'
import { FullscreenLibrarySettings } from './FullscreenLibrarySettings'
import selectGlyph from './assets/xbox_button_a_outline.svg?raw'
import backGlyph from './assets/xbox_button_b_outline.svg?raw'
import keyboardGlyph from './assets/xbox_button_y_outline.svg?raw'
import adjustGlyph from './assets/xbox_dpad.svg?raw'
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
const providers: Record<number, { title: string; label: string }> = {
  1: { title: 'IGDB metadata', label: 'Set up IGDB metadata' },
  2: { title: 'Steam', label: 'Set up Steam' },
  3: { title: 'Epic', label: 'Set up Epic' },
  4: { title: 'GOG', label: 'Check GOG Galaxy' },
  5: { title: 'Appearance', label: 'Choose theme and appearance' },
  6: { title: 'Application', label: 'Choose app settings' },
  7: { title: 'Library', label: 'Choose library settings' },
}

export function Setup({
  mode,
  onComplete,
  appearance,
  appearanceSaving = false,
  appearanceSaveError,
  onOpenChange,
  suspended = false,
}: {
  mode: Mode
  onComplete?: () => void
  appearance?: ReactNode
  appearanceSaving?: boolean
  appearanceSaveError?: string | null
  onOpenChange?: (open: boolean) => void
  suspended?: boolean
}) {
  const progress = useApiQuery<SetupProgress>('setup.get')
  // A failed replay write must still offer recovery without marking stored setup unfinished.
  const recovery = useQuery<SetupProgress | null>({
    queryKey: ['setup-recovery'],
    queryFn: async () => null,
    enabled: false,
    initialData: null,
  })
  const client = useQueryClient()
  const [failure, setFailure] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [editorOpen, setEditorOpen] = useState(false)
  const [providerPreferenceFailed, setProviderPreferenceFailed] = useState(false)
  const [busyEditors, setBusyEditors] = useState<Set<string>>(() => new Set())
  const [failedPreferences, setFailedPreferences] = useState<Set<string>>(() => new Set())
  const savingRef = useRef(false)
  const nextButton = useRef<HTMLButtonElement>(null)
  const providerButton = useRef<HTMLButtonElement>(null)
  const providerBack = useRef<HTMLButtonElement>(null)
  const providerBody = useRef<HTMLDivElement>(null)
  const invokingControl = useRef<HTMLElement | null>(null)
  const rememberInvoker = useCallback(() => {
    const active = document.activeElement
    if (
      active instanceof HTMLElement &&
      active !== document.body &&
      !active.closest('[role="dialog"], [role="alertdialog"]')
    )
      invokingControl.current = active
  }, [])
  const step = recovery.data?.step ?? (progress.error ? 0 : progress.data?.step)
  const open = !suspended && typeof step === 'number'
  const safeStep =
    typeof step === 'number' && Number.isInteger(step) && step >= 0 && step < steps.length ? step : 0
  const appearancePending = safeStep === 5 && appearanceSaving
  const busy = saving || busyEditors.size > 0 || appearancePending
  const appearanceFailure = safeStep === 5 ? appearanceSaveError : null
  const provider = providers[safeStep]
  const providerOpen = open && mode === 'fullscreen' && !!provider && editorOpen
  function closeProvider() {
    setProviderPreferenceFailed(failedPreferences.size > 0)
    setEditorOpen(false)
  }
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
    if (open || suspended) return
    rememberInvoker()
    // A pending replay disables its button before the progress write opens setup.
    document.addEventListener('focusin', rememberInvoker)
    return () => document.removeEventListener('focusin', rememberInvoker)
  }, [open, suspended, rememberInvoker])
  useEffect(() => {
    setEditorOpen(false)
    setProviderPreferenceFailed(false)
  }, [mode, safeStep, open])
  useEffect(() => {
    if (open) {
      rememberInvoker()
      if (providerOpen)
        (
          providerBody.current?.querySelector<HTMLElement>(
            'button:not(:disabled),input:not(:disabled),select:not(:disabled),textarea:not(:disabled)',
          ) ?? providerBack.current
        )?.focus()
      else if (mode === 'fullscreen' && provider) providerButton.current?.focus()
      else nextButton.current?.focus()
    }
  }, [safeStep, mode, open, provider, providerOpen, rememberInvoker])
  async function move(cursor: number | null, continueStep = false) {
    if (savingRef.current || busyEditors.size || appearancePending) return
    if (continueStep && (failedPreferences.size || providerPreferenceFailed || appearanceFailure)) {
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
      client.setQueryData(['setup-recovery'], null)
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
          data-setup-provider={providerOpen ? safeStep : undefined}
          aria-label="Winnow setup"
          onEscapeKeyDown={(event) => {
            event.preventDefault()
            // The TV back action returns through the wizard. Desktop Escape skips an optional step.
            if (busy) return
            if (providerOpen) closeProvider()
            else if (mode === 'fullscreen' && safeStep > 0) void move(safeStep - 1)
            else if (safeStep > 0 && safeStep < 8) void move(safeStep + 1)
          }}
          onInteractOutside={(event) => event.preventDefault()}
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            rememberInvoker()
            if (mode === 'fullscreen' && provider) providerButton.current?.focus()
            else nextButton.current?.focus()
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            if (!suspended && invokingControl.current?.isConnected) invokingControl.current.focus()
          }}
        >
          {mode === 'fullscreen' && <AvalonAmbientBackdrop page="settings" />}
          <div className="setup-content-clip">
            <header className="setup-header">
              <p className="eyebrow" aria-live="polite">
                SETUP · {safeStep + 1} OF {steps.length}
              </p>
              <Dialog.Title>{providerOpen ? provider.title : steps[safeStep][0]}</Dialog.Title>
              <Dialog.Description>{steps[safeStep][1]}</Dialog.Description>
            </header>
            <div
              ref={providerBody}
              className={`setup-body${safeStep === 1 && mode === 'desktop' ? ' setup-body-igdb' : ''}`}
              key={`${mode}:${safeStep}:${providerOpen}`}
            >
              <SetupBusyContext.Provider value={reportBusy}>
                <SetupErrorContext.Provider value={reportError}>
                  {mode === 'fullscreen' && provider && !providerOpen ? (
                    <>
                      <SetupProviderStatus step={safeStep} />
                      <button
                        ref={providerButton}
                        className="fullscreen-setting-row"
                        disabled={busy}
                        onClick={() => {
                          setProviderPreferenceFailed(false)
                          setEditorOpen(true)
                        }}
                      >
                        {provider.label}
                      </button>
                    </>
                  ) : (
                    <>
                      {safeStep === 0 && (
                        <p className="setup-welcome">
                          Your games. One library. Something worth coming back to.
                        </p>
                      )}
                      {safeStep === 1 && <SetupIgdb mode={mode} />}
                      {safeStep === 2 && <SetupSteam mode={mode} />}
                      {safeStep === 3 && <SetupEpic mode={mode} />}
                      {safeStep === 4 && (
                        <p>Galaxy discovery runs in the background. Continue whenever you are ready.</p>
                      )}
                      {safeStep === 5 &&
                        (appearance ?? <p>Open Theme Studio after setup to choose your appearance.</p>)}
                      {safeStep === 6 && <ApplicationPreferences setup mode={mode} />}
                      {safeStep === 7 && <SetupLibrary mode={mode} />}
                      {safeStep === 8 && <p>Anything you skipped remains available in Settings.</p>}
                    </>
                  )}
                </SetupErrorContext.Provider>
              </SetupBusyContext.Provider>
            </div>
            <footer className="setup-footer">
              {providerOpen ? (
                <button ref={providerBack} disabled={busy} aria-label="Back to setup" onClick={closeProvider}>
                  Back
                </button>
              ) : (
                <>
                  <Notice
                    message={
                      failure ||
                      appearanceFailure ||
                      recovery.data?.problem ||
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
                </>
              )}
              {mode === 'fullscreen' && (
                <SetupHints
                  provider={providerOpen}
                  back={safeStep > 0}
                  busy={busy}
                  appearance={providerOpen && safeStep === 5}
                />
              )}
            </footer>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

function SetupHints({
  provider,
  back,
  busy,
  appearance,
}: {
  provider: boolean
  back: boolean
  busy: boolean
  appearance: boolean
}) {
  const root = useRef<HTMLDivElement>(null)
  const [contextLabel, setContextLabel] = useState<string | null>(null)
  useEffect(() => {
    const update = () => {
      const active = document.activeElement
      const surface = root.current?.closest('.setup-dialog')
      const keyboard =
        provider &&
        !busy &&
        editable(active) &&
        !active.disabled &&
        !active.readOnly &&
        !!surface?.contains(active)
      setContextLabel(
        keyboard
          ? 'Keyboard'
          : appearance && !busy && surface?.querySelector('button[data-controller-context]:not(:disabled)')
            ? 'Reset page'
            : null,
      )
    }
    update()
    document.addEventListener('focusin', update)
    document.addEventListener('focusout', update)
    const observer = new MutationObserver(update)
    const surface = root.current?.closest('.setup-dialog')
    if (surface)
      observer.observe(surface, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ['disabled'],
      })
    return () => {
      document.removeEventListener('focusin', update)
      document.removeEventListener('focusout', update)
      observer.disconnect()
    }
  }, [provider, busy, appearance])
  const hints = [
    ...(appearance ? [[adjustGlyph, 'Left / Right', 'Adjust']] : []),
    [selectGlyph, 'A', 'Select'],
    ...(back ? [[backGlyph, 'B', provider ? 'Back' : 'Previous step']] : []),
    ...(contextLabel ? [[keyboardGlyph, 'Y', contextLabel]] : []),
  ]
  return (
    <div ref={root} className="setup-controller-hints" role="group" aria-label="Setup controls">
      {hints.map(([art, key, label]) => (
        <span key={key}>
          <span
            aria-hidden="true"
            data-setup-glyph={key}
            dangerouslySetInnerHTML={{
              __html: art.replace('<svg ', '<svg viewBox="8 8 48 48" '),
            }}
          />
          <span className="sr-only">{key} </span>
          {label}
        </span>
      ))}
    </div>
  )
}

function SetupProviderStatus({ step }: { step: number }) {
  if (step === 1) return <SetupIgdbStatus />
  if (step === 2 || step === 3) return <SetupStoreStatus step={step} />
  if (step === 4)
    return (
      <p className="setup-provider-status">Galaxy discovery runs in the background. No sign-in is needed.</p>
    )
  return null
}
function SetupIgdbStatus() {
  const connection = useApiQuery<IgdbConnection>('connections.igdb.get')
  return (
    <p className="setup-provider-status" role="status">
      {connection.data ? igdbSavedStatus(connection.data) : 'Open IGDB metadata to check your credentials.'}
    </p>
  )
}
function SetupStoreStatus({ step }: { step: number }) {
  const stores = useApiQuery<StoreConnections>('connections.get')
  const status = !stores.data
    ? 'Open the provider to check your connection.'
    : step === 2
      ? steamConnectionState(stores.data).healthMessage
      : stores.data.epic?.isLive
        ? 'Signed in to Epic.'
        : stores.data.epic
          ? 'Your Epic sign-in has expired.'
          : 'Installed games are available through the local Epic library.'
  return (
    <p className="setup-provider-status" role="status">
      {status}
    </p>
  )
}

function SetupIgdb({ mode }: { mode: Mode }) {
  return (
    <div className={`setup-igdb${mode === 'fullscreen' ? ' fullscreen-information-column' : ''}`}>
      <IgdbConnectionPanel mode={mode} bounded={mode === 'desktop'} sectioned={mode === 'fullscreen'} />
      <p className="muted">
        Saved IGDB credentials take effect immediately. Metadata fills in in the background. Continue does not
        save unsaved credentials.
      </p>
    </div>
  )
}
function SetupSteam({ mode }: { mode: Mode }) {
  const stores = useApiQuery<StoreConnections>('connections.get')
  return (
    <>
      <Notice error={stores.error} />
      {stores.data && <SteamConnectionCard snapshot={stores.data} mode={mode} purchase={false} />}
    </>
  )
}
function SetupEpic({ mode }: { mode: Mode }) {
  const stores = useApiQuery<StoreConnections>('connections.get')
  return (
    <>
      <Notice error={stores.error} />
      {stores.data && <EpicConnectionCard snapshot={stores.data} mode={mode} />}
    </>
  )
}
function SetupLibrary({ mode }: { mode: Mode }) {
  return mode === 'fullscreen' ? <FullscreenLibrarySettings setup /> : <SetupDesktopLibrary />
}
function SetupDesktopLibrary() {
  const preferences = useApiQuery<LibraryPreferences>('preferences.library.get')
  return (
    <>
      <Notice error={preferences.error} />
      {preferences.data && <LibraryPreferenceForm initial={preferences.data} />}
      <RatingCapPreference mode="desktop" />
      <LibraryPresentationPreferences />
    </>
  )
}

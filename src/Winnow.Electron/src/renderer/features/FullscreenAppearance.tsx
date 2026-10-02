import { useEffect, useRef, useState, type ReactNode } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useQueryClient } from '@tanstack/react-query'
import { request } from '../api/client'
import { usePresentationPreferences } from './SettingsPreferences'
import { FullscreenAdjustment, FullscreenSwitch } from './FullscreenSettingRows'
import { Notice } from './shared'
import { useLibrary } from '../api/hooks'
import { Artwork } from '../components/Artwork'
import { dormancy } from '../themes/avalon-data'
import { useSetupBusy, useSetupPreferenceError } from './settingsState'

export function boundedSetting(value: string | null | undefined, fallback: number, min: number, max: number) {
  if (value == null || !value.trim()) return fallback
  const number = Number(value)
  return Number.isFinite(number) ? Math.max(min, Math.min(max, number)) : fallback
}
const resetValues = {
  FullscreenTextScale: '1',
  FullscreenInterfaceScale: '1',
  FullscreenSafeMargin: '5',
  FullscreenReducedMotion: 'false',
  FullscreenFitUltrawide: 'false',
}

export function FullscreenAppearance({
  themeControls,
  setup = false,
}: {
  themeControls?: ReactNode
  setup?: boolean
}) {
  const preferences = usePresentationPreferences(),
    client = useQueryClient()
  const [confirm, setConfirm] = useState(false),
    [resetting, setResetting] = useState(false),
    [failure, setFailure] = useState<unknown>(null)
  const resetButton = useRef<HTMLButtonElement>(null)
  const surface = useRef<HTMLElement>(null)
  const disabled = !preferences.loaded || preferences.pending || resetting
  useSetupBusy(preferences.pending || resetting || confirm)
  useSetupPreferenceError(preferences.error || failure)
  useEffect(() => {
    if (!preferences.loaded) return
    const timer = setTimeout(() => {
      const active = document.activeElement
      if (surface.current?.closest('[inert]')) return
      if (
        active === document.body ||
        active?.id === 'main-content' ||
        active?.closest('.settings-page [aria-label="Settings section"]')
      )
        surface.current
          ?.querySelector<HTMLButtonElement>('[data-fullscreen-settings-initial]')
          ?.focus({ preventScroll: true })
    }, 0)
    return () => clearTimeout(timer)
  }, [preferences.loaded])
  async function reset() {
    setResetting(true)
    setFailure(null)
    try {
      for (const [preference, value] of Object.entries(resetValues))
        await request('preferences.presentation.put', { preference }, { value })
      setConfirm(false)
    } catch (error) {
      setFailure(error)
    } finally {
      await client.invalidateQueries({ queryKey: ['api', 'preferences.presentation.get'] })
      setResetting(false)
    }
  }
  return (
    <div className="fullscreen-appearance-layout">
      <section ref={surface} className="fullscreen-settings-content" aria-label="Fullscreen appearance">
        <h2 className="fullscreen-settings-group">Display</h2>
        {(
          [
            [
              'FullscreenTextScale',
              'Text size',
              'Adjust until this reads comfortably from your seat.',
              0.7,
              1.4,
              0.1,
              1,
              true,
            ],
            [
              'FullscreenInterfaceScale',
              'Interface scale',
              'Resize covers, controls and text together.',
              0.8,
              1.2,
              0.05,
              1,
              true,
            ],
            [
              'FullscreenSafeMargin',
              'Screen margins',
              'Keep important content within your TV’s safe area.',
              0,
              10,
              1,
              5,
              false,
            ],
          ] as const
        ).map(([key, label, description, min, max, step, fallback, steppers], index) => {
          const value = boundedSetting(preferences.values[key], fallback, min, max)
          return (
            <FullscreenAdjustment
              key={key}
              label={label}
              description={description}
              value={`${Math.round(value * (key === 'FullscreenSafeMargin' ? 1 : 100))}%`}
              disabled={disabled}
              steppers={steppers}
              initial={index === 0}
              change={(direction) => {
                const next = Math.max(min, Math.min(max, Math.round((value + direction * step) * 100) / 100))
                if (next !== value) preferences.set(key, String(next))
              }}
            />
          )
        })}
        {(
          [
            [
              'FullscreenFitUltrawide',
              'Fit ultrawide displays',
              'Use the full width of your display.',
              false,
            ],
            ['FullscreenReducedMotion', 'Reduce motion', 'Minimise animations and motion effects.', false],
            [
              'DimDormantCovers',
              'Dim dormant covers',
              'Slightly dim games you haven’t played recently.',
              true,
            ],
          ] as const
        ).map(([key, label, description, fallback]) => (
          <FullscreenSwitch
            key={key}
            label={label}
            description={description}
            value={
              fallback
                ? preferences.values[key]?.trim().toLowerCase() !== 'false'
                : preferences.values[key]?.trim().toLowerCase() === 'true'
            }
            disabled={disabled}
            change={(value) => preferences.set(key, String(value))}
          />
        ))}
        <FullscreenAdjustment
          label="Cover art"
          description="Fit shows the whole image. Fill crops it to the card."
          value={preferences.values.CoverArtMode === 'fill' ? 'Fill' : 'Fit'}
          disabled={disabled}
          change={() =>
            preferences.set('CoverArtMode', preferences.values.CoverArtMode === 'fill' ? 'fit' : 'fill')
          }
        />
        {themeControls}
        <button
          ref={resetButton}
          data-controller-context
          disabled={disabled}
          onClick={() => {
            setFailure(null)
            setConfirm(true)
          }}
        >
          Reset fullscreen appearance…
        </button>
        <Notice error={preferences.error} />
        <Dialog.Root
          open={confirm}
          onOpenChange={(open) => {
            if (!resetting) setConfirm(open)
          }}
        >
          <Dialog.Portal>
            <Dialog.Overlay className={`dialog-overlay${setup ? ' setup-nested-overlay' : ''}`} />
            <Dialog.Content
              className={`dialog-content fullscreen-settings-reset${setup ? ' setup-nested-dialog' : ''}`}
              role="alertdialog"
              aria-describedby="fullscreen-reset-description"
              onCloseAutoFocus={(event) => {
                event.preventDefault()
                resetButton.current?.focus()
              }}
            >
              <Dialog.Title>Reset fullscreen appearance?</Dialog.Title>
              <Dialog.Description id="fullscreen-reset-description">
                Reset fullscreen text size, interface scale, screen margins, reduced motion and ultrawide fit?
                Your shared theme and cover dimming stay as they are.
              </Dialog.Description>
              <div className="form-actions">
                <button disabled={resetting} onClick={() => setConfirm(false)}>
                  Cancel
                </button>
                <button
                  disabled={resetting}
                  onClick={() => {
                    void reset()
                  }}
                >
                  Reset fullscreen appearance
                </button>
              </div>
              <Notice error={failure} />
            </Dialog.Content>
          </Dialog.Portal>
        </Dialog.Root>
      </section>
      <FullscreenSettingsPreview />
    </div>
  )
}

export function FullscreenSettingsPreview({
  section = 'Appearance',
}: {
  section?: 'Appearance' | 'Library' | 'Application' | 'Metadata & artwork'
}) {
  const preferences = usePresentationPreferences()
  const library = useLibrary()
  const sample = section === 'Appearance' ? library.data?.games[0] : undefined
  const ramp = dormancy(sample?.lastPlayedAt)
  return (
    <aside className="fullscreen-settings-preview" aria-label={`${section} preview`}>
      <h2 className="fullscreen-settings-group">{section === 'Appearance' ? 'Preview' : section}</h2>
      {sample && (
        <div
          className="fullscreen-settings-preview-cover"
          style={{
            filter:
              preferences.values.DimDormantCovers?.trim().toLowerCase() === 'false'
                ? 'none'
                : `saturate(${ramp.saturation}) hue-rotate(${ramp.hue}deg) brightness(${ramp.brightness})`,
          }}
        >
          <Artwork workId={sample.headerWorkId ?? sample.workId} />
        </div>
      )}
      <h3>{sample?.title ?? 'Your next game is already here.'}</h3>
      <p>
        {section === 'Appearance'
          ? 'Theme, typography, cover art and cover dimming apply to both views. Other appearance settings apply to fullscreen.'
          : 'Library and account settings apply to both desktop and fullscreen.'}
      </p>
    </aside>
  )
}

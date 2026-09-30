import { useId, useLayoutEffect, useRef } from 'react'
import { useIsMutating, useMutation, useQueryClient } from '@tanstack/react-query'
import { request } from '../api/client'
import { useApiQuery } from '../api/hooks'
import type { LibraryPreferences, Mode } from '../api/types'
import { Notice } from './shared'
import './rating-cap.css'
import { useSetupBusy, useSetupPreferenceError } from './settingsState'

export const ratingCapSteps = [
  ['everyone', 'All ages'],
  ['preteen', 'Preteen'],
  ['teen', 'Teen'],
  ['mature', 'Mature'],
  ['restricted18', '18+'],
  ['adults_only', 'Adults only'],
] as const

export const ratingCapIndex = (value?: string | null) => {
  const index = ratingCapSteps.findIndex(([tier]) => tier === value?.trim().toLowerCase())
  return index < 0 ? ratingCapSteps.length - 1 : index
}
export const ratingCapAt = (position: number) =>
  ratingCapSteps[Math.max(0, Math.min(ratingCapSteps.length - 1, Math.round(position))) || 0]![0]
export const ratingCapHiddenText = (count: number) =>
  count <= 0
    ? 'No titles hidden.'
    : count === 1
      ? 'Hiding 1 title.'
      : `Hiding ${count.toLocaleString()} titles.`

export function RatingCapControl({
  value,
  adultContentAllowed = false,
  hiddenCount,
  countFailed = false,
  disabled = false,
  change,
  mode = 'desktop',
}: {
  value?: string | null
  adultContentAllowed?: boolean
  hiddenCount?: number
  countFailed?: boolean
  disabled?: boolean
  change(value: string): void
  mode?: Mode
}) {
  const focusAfterSave = useRef<HTMLInputElement | null>(null)
  useLayoutEffect(() => {
    if (disabled) {
      const cancel = () => {
        focusAfterSave.current = null
      }
      document.addEventListener('pointerdown', cancel, true)
      document.addEventListener('keydown', cancel, true)
      return () => {
        document.removeEventListener('pointerdown', cancel, true)
        document.removeEventListener('keydown', cancel, true)
      }
    }
    const control = focusAfterSave.current
    focusAfterSave.current = null
    // Chromium blurs a disabled range. A later user action owns focus instead.
    if (control?.isConnected && document.activeElement === document.body)
      control.focus({ preventScroll: true })
  }, [disabled, value])
  const id = useId(),
    index = ratingCapIndex(value),
    label = ratingCapSteps[index]![1]
  return (
    <section className={`rating-cap mode-${mode}`} aria-label="Rating cap preference">
      <label htmlFor={id} className="rating-cap-heading">
        <span>{mode === 'fullscreen' ? 'Content age limit' : 'Rating cap'}</span>
        <span>{label}</span>
      </label>
      <input
        id={id}
        aria-label={mode === 'fullscreen' ? 'Content age limit' : 'Rating cap'}
        aria-valuetext={label}
        aria-describedby={`${id}-help ${id}-count${index === 5 && !adultContentAllowed ? ` ${id}-clamp` : ''}`}
        type="range"
        min={0}
        max={5}
        step={1}
        value={index}
        disabled={disabled}
        onChange={(event) => {
          focusAfterSave.current = document.activeElement === event.currentTarget ? event.currentTarget : null
          change(ratingCapAt(Number(event.target.value)))
        }}
      />
      <p id={`${id}-help`}>Hides games rated above this level. Unrated games always stay.</p>
      <p id={`${id}-count`} role="status">
        {countFailed
          ? 'Hidden-title count is unavailable.'
          : hiddenCount === undefined
            ? 'Counting hidden titles…'
            : ratingCapHiddenText(hiddenCount)}
      </p>
      {index === 5 && !adultContentAllowed && (
        <p id={`${id}-clamp`}>Adults-only content is still hidden by the toggle in Settings › Library.</p>
      )}
    </section>
  )
}

/** The library preference PUT replaces the complete object; preserve other clients' latest fields. */
export function useLibraryPreferenceChange() {
  const client = useQueryClient()
  const pending = useIsMutating({ mutationKey: ['library-preferences'] }) > 0
  const mutation = useMutation({
    mutationKey: ['library-preferences'],
    scope: { id: 'library-preferences' },
    retry: false,
    mutationFn: async ({ field, value }: { field: keyof LibraryPreferences; value: boolean | string }) => {
      const latest = await request<LibraryPreferences>('preferences.library.get')
      const saved = { ...latest, [field]: value }
      await request('preferences.library.put', undefined, saved)
      await client.cancelQueries({ queryKey: ['api', 'preferences.library.get'] })
      client.setQueryData(['api', 'preferences.library.get', undefined], saved)
    },
    onSettled: () => client.invalidateQueries({ queryKey: ['api'] }),
  })
  return {
    apply: (field: keyof LibraryPreferences, value: boolean | string) => mutation.mutate({ field, value }),
    pending,
    error: mutation.error,
  }
}

export function RatingCapPreference({ mode = 'desktop' }: { mode?: Mode }) {
  const preferences = useApiQuery<LibraryPreferences>('preferences.library.get')
  const counts = useApiQuery<{ ratingCapHidden: number }>('library.visibility')
  const change = useLibraryPreferenceChange()
  useSetupBusy(change.pending)
  useSetupPreferenceError(preferences.error || change.error)
  return (
    <>
      <RatingCapControl
        mode={mode}
        value={preferences.data?.maturityCap}
        adultContentAllowed={preferences.data?.showExplicitContent}
        hiddenCount={counts.data?.ratingCapHidden}
        countFailed={!!counts.error}
        disabled={!preferences.data || change.pending}
        change={(value) => {
          void change.apply('maturityCap', value)
        }}
      />
      <Notice error={preferences.error || change.error} />
      {!!(preferences.error || counts.error) && (
        <button
          onClick={() => {
            void preferences.refetch()
            void counts.refetch()
          }}
        >
          Retry rating preference
        </button>
      )}
    </>
  )
}

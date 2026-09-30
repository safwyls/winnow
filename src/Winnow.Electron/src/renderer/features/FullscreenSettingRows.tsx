import { useEffect, useId, useLayoutEffect, useRef, type ReactNode } from 'react'
import './fullscreen-settings.css'

export function useFullscreenSettingsEntry(ready: boolean, entryKey?: string) {
  const surface = useRef<HTMLElement>(null)
  useEffect(() => {
    if (!ready) return
    const timer = setTimeout(() => {
      const active = document.activeElement
      if (surface.current?.closest('[inert]')) return
      if (
        active === document.body ||
        active?.id === 'main-content' ||
        active?.closest('.settings-page > .tabs')
      )
        surface.current
          ?.querySelector<HTMLButtonElement>('button:not(:disabled)')
          ?.focus({ preventScroll: true })
    }, 0)
    return () => clearTimeout(timer)
  }, [ready, entryKey])
  return surface
}

function useSettingFocus(disabled: boolean) {
  const row = useRef<HTMLButtonElement>(null)
  const restore = useRef(false)
  useLayoutEffect(() => {
    if (disabled) {
      const cancel = () => {
        restore.current = false
      }
      document.addEventListener('pointerdown', cancel, true)
      document.addEventListener('keydown', cancel, true)
      return () => {
        document.removeEventListener('pointerdown', cancel, true)
        document.removeEventListener('keydown', cancel, true)
      }
    }
    if (restore.current && document.activeElement === document.body)
      row.current?.focus({ preventScroll: true })
    restore.current = false
  }, [disabled])
  return {
    row,
    remember: () => {
      restore.current = document.activeElement === row.current
    },
  }
}

export function FullscreenAdjustment({
  label,
  description,
  value,
  disabled = false,
  change,
  steppers = false,
  initial = false,
  describedBy,
}: {
  label: string
  description: string
  value: string
  disabled?: boolean
  change(direction: -1 | 1): void
  steppers?: boolean
  initial?: boolean
  describedBy?: string
}) {
  const id = useId(),
    focus = useSettingFocus(disabled)
  const adjust = (direction: -1 | 1) => {
    if (disabled) return
    focus.remember()
    change(direction)
  }
  return (
    <div className="fullscreen-setting-adjustment">
      <button
        ref={focus.row}
        className="fullscreen-setting-row"
        aria-label={label}
        aria-describedby={`${id}-description ${id}-value${describedBy ? ` ${describedBy}` : ''}`}
        disabled={disabled}
        data-fullscreen-settings-initial={initial || undefined}
        onClick={() => adjust(1)}
        onKeyDown={(event) => {
          if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
          event.preventDefault()
          event.stopPropagation()
          adjust(event.key === 'ArrowLeft' ? -1 : 1)
        }}
      >
        <span className="fullscreen-setting-copy">
          <strong>{label}</strong>
          <span id={`${id}-description`}>{description}</span>
        </span>
        <span className="fullscreen-setting-value">
          <span aria-hidden="true">‹</span>
          <span id={`${id}-value`}>{value}</span>
          <span aria-hidden="true">›</span>
        </span>
      </button>
      {steppers &&
        ([-1, 1] as const).map((direction) => (
          <button
            key={direction}
            type="button"
            tabIndex={-1}
            className="fullscreen-setting-step"
            aria-label={`${direction < 0 ? 'Decrease' : 'Increase'} ${label.toLowerCase()}`}
            disabled={disabled}
            onMouseDown={(event) => event.preventDefault()}
            onClick={() => adjust(direction)}
          >
            {direction < 0 ? '−' : '+'}
          </button>
        ))}
    </div>
  )
}

export function FullscreenSwitch({
  label,
  description,
  value,
  disabled = false,
  change,
}: {
  label: string
  description: string
  value: boolean
  disabled?: boolean
  change(value: boolean): void
}) {
  const id = useId(),
    focus = useSettingFocus(disabled)
  const set = (next: boolean) => {
    if (disabled || next === value) return
    focus.remember()
    change(next)
  }
  return (
    <button
      ref={focus.row}
      type="button"
      className="fullscreen-setting-row fullscreen-setting-switch"
      role="switch"
      aria-checked={value}
      aria-label={label}
      aria-describedby={`${id}-description ${id}-value`}
      disabled={disabled}
      onClick={() => set(!value)}
      onKeyDown={(event) => {
        if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
        event.preventDefault()
        event.stopPropagation()
        set(event.key === 'ArrowRight')
      }}
    >
      <span className="fullscreen-setting-copy">
        <strong>{label}</strong>
        <span id={`${id}-description`}>{description}</span>
      </span>
      <span className="fullscreen-setting-switch-value">
        <span id={`${id}-value`}>{value ? 'On' : 'Off'}</span>
        <span className="fullscreen-switch-track" aria-hidden="true">
          <span />
        </span>
      </span>
    </button>
  )
}

export function FullscreenSettingsAction({
  label,
  value,
  children,
  onClick,
  kind = 'Open',
  disabled = false,
}: {
  label: string
  value?: string
  children?: ReactNode
  onClick(): void
  kind?: 'Open' | 'Run' | 'Browser'
  disabled?: boolean
}) {
  return (
    <button
      type="button"
      className="fullscreen-setting-row fullscreen-setting-action"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
    >
      <span>
        {label}
        {value && <span className="fullscreen-setting-current">{value}</span>}
        {children}
      </span>
      <span className="fullscreen-setting-cue" aria-hidden="true">
        {kind} <span>{kind === 'Open' ? '›' : kind === 'Run' ? 'Ⓐ' : '↗'}</span>
      </span>
    </button>
  )
}

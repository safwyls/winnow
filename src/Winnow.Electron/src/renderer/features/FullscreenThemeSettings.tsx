import { useRef, useState, type ReactNode } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { avalonPaletteId, resolvedTypography, typographyKey } from '../../shared/theme'
import { DEFAULT_TYPOGRAPHY, validFontFamily, type ThemeTypography } from '../../shared/typography'
import type { ThemeRuntime } from '../theming/runtime'
import { avalonPalettes } from '../themes/avalon-palettes'
import { FullscreenAdjustment, FullscreenSettingsAction } from './FullscreenSettingRows'
import { useSetupBusy, useSetupPreferenceError } from './settingsState'
import { Notice } from './shared'
import selectGlyph from './assets/xbox_button_a_outline.svg?raw'
import backGlyph from './assets/xbox_button_b_outline.svg?raw'
import keyboardGlyph from './assets/xbox_button_y_outline.svg?raw'

export function FullscreenThemeSettings({
  runtime,
  openStudio,
  setup = false,
}: {
  runtime: ThemeRuntime
  openStudio?(): void
  setup?: boolean
}) {
  const { profile, setProfile } = runtime,
    typography = resolvedTypography(profile)
  const [picker, setPicker] = useState<
    'design' | 'theme' | 'headingFont' | 'interfaceFont' | 'dataFont' | null
  >(null)
  useSetupBusy(!!picker || runtime.profileSaving || runtime.loading)
  useSetupPreferenceError(runtime.profileSaveError)
  const [fonts, setFonts] = useState<string[]>(
    Object.values(DEFAULT_TYPOGRAPHY).filter((value): value is string => typeof value === 'string'),
  )
  const [notice, setNotice] = useState(''),
    [draft, setDraft] = useState('')
  const [fontInputFocused, setFontInputFocused] = useState(false)
  const pickerOrigin = useRef<HTMLElement | null>(null)
  const key = typographyKey(profile),
    palette = avalonPaletteId(profile)
  const palettes = [
    ...avalonPalettes().map(({ id, name }) => ({ id, name })),
    { id: 'profile', name: 'Studio colors' },
  ]
  const roleNames = {
    headingFont: 'Heading font',
    interfaceFont: 'Interface font',
    dataFont: 'Data font',
  } as const
  const changeTypography = (patch: Partial<ThemeTypography>) =>
    setProfile((current) => ({
      ...current,
      appearance: {
        ...current.appearance,
        typography: {
          ...current.appearance.typography,
          [typographyKey(current)]: { ...resolvedTypography(current), ...patch },
        },
      },
    }))
  async function chooseFont(role: keyof typeof roleNames) {
    pickerOrigin.current = document.activeElement as HTMLElement | null
    setNotice('')
    setDraft('')
    setFontInputFocused(false)
    setPicker(role)
    try {
      const installed = await window.winnow.listFonts?.()
      if (installed?.length)
        setFonts((current) =>
          [...new Set([...current, ...installed.filter(validFontFamily)])].sort((a, b) => a.localeCompare(b)),
        )
    } catch {
      setNotice('Installed fonts could not be read. You can enter a font family below.')
    }
  }
  let content: ReactNode = null
  if (picker === 'design')
    content = runtime.builtins.map((theme) => (
      <button
        key={theme.id}
        type="button"
        aria-pressed={profile.themeId === theme.id}
        onClick={() => {
          runtime.selectTheme(theme.id)
          setPicker(null)
        }}
      >
        {theme.name}
      </button>
    ))
  else if (picker === 'theme')
    content = palettes.map(({ id, name }) => (
      <button
        key={id}
        type="button"
        aria-pressed={palette === id}
        onClick={() => {
          setProfile((current) => ({
            ...current,
            settings: { ...current.settings, avalon: { ...current.settings.avalon, palette: id } },
          }))
          setPicker(null)
        }}
      >
        {name}
      </button>
    ))
  else if (picker)
    content = (
      <>
        <div className="fullscreen-font-options">
          {[...new Set([typography[picker], ...fonts])].map((font) => (
            <button
              key={font}
              type="button"
              aria-pressed={typography[picker] === font}
              onClick={() => {
                changeTypography({ [picker]: font })
                setPicker(null)
              }}
            >
              {font}
            </button>
          ))}
        </div>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            if (validFontFamily(draft)) {
              changeTypography({ [picker]: draft.trim() })
              setPicker(null)
            }
          }}
        >
          <label className="field">
            Font family
            <input
              value={draft}
              maxLength={128}
              onChange={(event) => setDraft(event.target.value)}
              onFocus={() => setFontInputFocused(true)}
              onBlur={() => setFontInputFocused(false)}
            />
          </label>
          <button disabled={!validFontFamily(draft)}>Use font</button>
        </form>
        {notice && <p role="status">{notice}</p>}
      </>
    )
  return (
    <>
      <h2 className="fullscreen-settings-group">Theme</h2>
      {setup && (
        <FullscreenSettingsAction
          label="Winnow design"
          value={runtime.theme.name}
          onClick={() => {
            pickerOrigin.current = document.activeElement as HTMLElement | null
            setPicker('design')
          }}
        />
      )}
      {profile.themeId === 'avalon' && (
        <FullscreenSettingsAction
          label="Theme"
          value={palettes.find((item) => item.id === palette)?.name ?? palette}
          onClick={() => {
            pickerOrigin.current = document.activeElement as HTMLElement | null
            setPicker('theme')
          }}
        />
      )}
      {openStudio && (
        <FullscreenSettingsAction label="Theme Studio" value={runtime.theme.name} onClick={openStudio} />
      )}
      <h2 className="fullscreen-settings-group">Typography</h2>
      <p className="fullscreen-settings-sample">
        Fonts and text size are saved with this theme. Missing fonts use the bundled face for that role.
      </p>
      {(Object.entries(roleNames) as [keyof typeof roleNames, string][]).map(([role, label]) => (
        <FullscreenSettingsAction
          key={role}
          label={label}
          value={typography[role]}
          onClick={() => {
            void chooseFont(role)
          }}
        />
      ))}
      <FullscreenAdjustment
        label="Theme text size"
        description="Saved with the theme. Combines with your fullscreen text size."
        value={`${typography.sizePercent}%`}
        steppers
        change={(direction) =>
          changeTypography({
            sizePercent: Math.max(80, Math.min(120, typography.sizePercent + 5 * direction)),
          })
        }
      />
      <FullscreenSettingsAction
        label="Reset theme typography"
        kind="Run"
        onClick={() =>
          setProfile((current) => {
            const remaining = { ...current.appearance.typography }
            delete remaining[key]
            return { ...current, appearance: { ...current.appearance, typography: remaining } }
          })
        }
      />
      <Notice error={runtime.profileSaveError ? new Error(runtime.profileSaveError) : undefined} />
      <Dialog.Root
        open={!!picker}
        onOpenChange={(open) => {
          if (!open) setPicker(null)
        }}
      >
        <Dialog.Portal>
          <Dialog.Overlay className={`dialog-overlay${setup ? ' setup-nested-overlay' : ''}`} />
          <Dialog.Content
            className={`dialog-content fullscreen-settings-picker${setup ? ' setup-nested-dialog' : ''}`}
            onCloseAutoFocus={(event) => {
              event.preventDefault()
              if (pickerOrigin.current?.isConnected) pickerOrigin.current.focus({ preventScroll: true })
            }}
          >
            <Dialog.Title>
              {picker === 'design'
                ? 'Winnow design'
                : picker === 'theme'
                  ? 'Theme'
                  : picker
                    ? roleNames[picker]
                    : 'Theme'}
            </Dialog.Title>
            <Dialog.Description>Choose a value to apply it to desktop and fullscreen.</Dialog.Description>
            <div className="fullscreen-settings-picker-body">{content}</div>
            <button onClick={() => setPicker(null)}>Back</button>
            <div className="fullscreen-settings-picker-hints" role="group" aria-label="Theme picker controls">
              {[
                [selectGlyph, 'A', 'Select'],
                [backGlyph, 'B', 'Back'],
                ...(fontInputFocused && picker !== 'theme' && picker !== 'design'
                  ? [[keyboardGlyph, 'Y', 'Keyboard']]
                  : []),
              ].map(([art, key, label]) => (
                <span key={key}>
                  <span
                    aria-hidden="true"
                    data-theme-picker-glyph={key}
                    dangerouslySetInnerHTML={{ __html: art.replace('<svg ', '<svg viewBox="8 8 48 48" ') }}
                  />
                  <span className="sr-only">{key} </span>
                  {label}
                </span>
              ))}
            </div>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </>
  )
}

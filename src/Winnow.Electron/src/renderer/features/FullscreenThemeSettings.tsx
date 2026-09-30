import { useRef, useState, type ReactNode } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { avalonPaletteId, resolvedTypography, typographyKey } from '../../shared/theme'
import { DEFAULT_TYPOGRAPHY, validFontFamily, type ThemeTypography } from '../../shared/typography'
import type { ThemeRuntime } from '../theming/runtime'
import { avalonPalettes } from '../themes/avalon-palettes'
import { FullscreenAdjustment, FullscreenSettingsAction } from './FullscreenSettingRows'

export function FullscreenThemeSettings({
  runtime,
  openStudio,
}: {
  runtime: ThemeRuntime
  openStudio(): void
}) {
  const { profile, setProfile } = runtime,
    typography = resolvedTypography(profile)
  const [picker, setPicker] = useState<'theme' | 'headingFont' | 'interfaceFont' | 'dataFont' | null>(null)
  const [fonts, setFonts] = useState<string[]>(
    Object.values(DEFAULT_TYPOGRAPHY).filter((value): value is string => typeof value === 'string'),
  )
  const [notice, setNotice] = useState(''),
    [draft, setDraft] = useState('')
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
  if (picker === 'theme')
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
            <input value={draft} maxLength={128} onChange={(event) => setDraft(event.target.value)} />
          </label>
          <button disabled={!validFontFamily(draft)}>Use font</button>
        </form>
        {notice && <p role="status">{notice}</p>}
      </>
    )
  return (
    <>
      <h2 className="fullscreen-settings-group">Theme</h2>
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
      <FullscreenSettingsAction label="Theme Studio" value={runtime.theme.name} onClick={openStudio} />
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
      <Dialog.Root
        open={!!picker}
        onOpenChange={(open) => {
          if (!open) setPicker(null)
        }}
      >
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content
            className="dialog-content fullscreen-settings-picker"
            onCloseAutoFocus={(event) => {
              event.preventDefault()
              if (pickerOrigin.current?.isConnected) pickerOrigin.current.focus({ preventScroll: true })
            }}
          >
            <Dialog.Title>{picker === 'theme' ? 'Theme' : picker ? roleNames[picker] : 'Theme'}</Dialog.Title>
            <Dialog.Description>Choose a value to apply it to desktop and fullscreen.</Dialog.Description>
            {content}
            <button onClick={() => setPicker(null)}>Back</button>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </>
  )
}

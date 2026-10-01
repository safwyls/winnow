import type { Mode } from '../api/types'
import { avalonPaletteId } from '../../shared/theme'
import type { ThemeRuntime } from '../theming/runtime'
import { AvalonAppearanceControls } from '../themes/avalon-appearance'
import { AvalonTypographyControls } from '../themes/avalon-typography'
import { avalonPalettes } from '../themes/avalon-palettes'
import { FullscreenAppearance } from './FullscreenAppearance'
import { FullscreenThemeSettings } from './FullscreenThemeSettings'
import { useSetupBusy, useSetupPreferenceError } from './settingsState'
import { Notice } from './shared'
import '../theming/studio.css'

export function SetupAppearance({ runtime, mode }: { runtime: ThemeRuntime; mode: Mode }) {
  useSetupBusy(mode === 'desktop' && (runtime.profileSaving || runtime.loading))
  useSetupPreferenceError(mode === 'desktop' ? runtime.profileSaveError : null)
  if (mode === 'fullscreen')
    return <FullscreenAppearance setup themeControls={<FullscreenThemeSettings runtime={runtime} setup />} />
  const { profile, setProfile } = runtime
  return (
    <div className="setup-appearance">
      <label className="field">
        Winnow design
        <select
          aria-label="Winnow design"
          value={profile.themeId}
          onChange={(event) => runtime.selectTheme(event.target.value)}
        >
          {runtime.builtins.map((theme) => (
            <option key={theme.id} value={theme.id}>
              {theme.name}
            </option>
          ))}
        </select>
      </label>
      {profile.themeId === 'avalon' && (
        <>
          <label className="field">
            Theme
            <select
              aria-label="Theme"
              value={avalonPaletteId(profile)}
              onChange={(event) => {
                const palette = event.target.value
                setProfile((current) => ({
                  ...current,
                  settings: { ...current.settings, avalon: { ...current.settings.avalon, palette } },
                }))
              }}
            >
              {avalonPalettes().map(({ id, name }) => (
                <option key={id} value={id}>
                  {name}
                </option>
              ))}
              <option value="profile">Studio colors</option>
            </select>
          </label>
          <AvalonAppearanceControls profile={profile} />
          <AvalonTypographyControls
            profile={profile}
            onChange={(typography) =>
              setProfile((current) => ({ ...current, appearance: { ...current.appearance, typography } }))
            }
          />
        </>
      )}
      <Notice error={runtime.profileSaveError ? new Error(runtime.profileSaveError) : undefined} />
    </div>
  )
}

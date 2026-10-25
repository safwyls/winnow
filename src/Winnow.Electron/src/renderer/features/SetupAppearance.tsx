import type { Mode } from '../api/types'
import { ChoiceMenu } from '../components/ChoiceMenu'
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
    return (
      <FullscreenAppearance
        setup
        compositionId={runtime.profile.themeId}
        themeControls={<FullscreenThemeSettings runtime={runtime} setup />}
      />
    )
  const { profile, setProfile } = runtime
  return (
    <div className="setup-appearance">
      <label className="field">
        Winnow design
        <ChoiceMenu
          label="Winnow design"
          aria-label="Winnow design"
          value={profile.themeId}
          onChange={(value) => runtime.selectTheme(value)}
          options={runtime.builtins.map((theme) => ({ value: theme.id, label: theme.name }))}
        />
      </label>
      {profile.themeId === 'avalon' && (
        <>
          <label className="field">
            Theme
            <ChoiceMenu
              label="Theme"
              aria-label="Theme"
              value={avalonPaletteId(profile)}
              onChange={(palette) => {
                setProfile((current) => ({
                  ...current,
                  settings: { ...current.settings, avalon: { ...current.settings.avalon, palette } },
                }))
              }}
              options={[
                ...avalonPalettes().map(({ id, name }) => ({ value: id, label: name })),
                { value: 'profile', label: 'Studio colors' },
              ]}
            />
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

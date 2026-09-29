// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { DEFAULT_PROFILE, selectThemeProfile } from '../src/shared/theme'
import { applyThemeProfile } from '../src/renderer/theming/runtime'
import { avalon } from '../src/renderer/themes/avalon'
import { afterglow } from '../src/renderer/themes/afterglow'

describe('Avalon portal palette', () => {
  it('applies a light original palette and bundled typography to the root that owns dialogs', () => {
    const root = document.createElement('div')
    const profile = selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon)
    profile.settings.avalon = { palette: 'silkcircuit-dawn' }
    applyThemeProfile(profile, root)
    expect(root.style.getPropertyValue('--bg')).toBe('#F4F0FA')
    expect(root.style.getPropertyValue('--accent')).toBe('#005F57')
    expect(root.style.getPropertyValue('--avalon-flare')).toBe('#C12A83')
    expect(root.style.getPropertyValue('--avalon-button-ink')).toBe('#FFFFFF')
    expect(root.style.getPropertyValue('--font-display')).toContain('Avalon Display')
    expect(root.style.getPropertyValue('--font-body')).toContain('Avalon Body')
    expect(root.style.colorScheme).toBe('light')
  })
  it('returns to Studio colors and another composition without leaking a former palette', () => {
    const root = document.createElement('div'),
      profile = selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon)
    profile.settings.avalon = { palette: 'rose-pine-dawn' }
    applyThemeProfile(profile, root)
    profile.settings.avalon.palette = 'profile'
    applyThemeProfile(profile, root)
    expect(root.style.getPropertyValue('--bg')).toBe('#0F1C1E')
    expect(root.style.colorScheme).toBe('dark')
    expect(root.style.getPropertyValue('--avalon-flare')).toBe('')
    applyThemeProfile(selectThemeProfile(profile, 'afterglow', afterglow), root)
    expect(root.style.getPropertyValue('--font-body')).not.toContain('Avalon')
    expect(root.style.getPropertyValue('--bg')).toBe('#18191b')
  })
})

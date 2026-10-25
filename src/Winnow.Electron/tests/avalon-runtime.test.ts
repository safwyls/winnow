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
  it('updates the existing root declaration and mounted controls without extending its palette variable schema', () => {
    const root = document.createElement('div')
    const control = document.createElement('button')
    control.textContent = 'Keep this control'
    root.append(control)
    root.style.setProperty('--private-view-value', 'retained')
    const profile = selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon)
    profile.settings.avalon = { palette: 'winnow' }
    applyThemeProfile(profile, root)
    const declaration = root.style
    const variables = Array.from({ length: declaration.length }, (_, index) => declaration.item(index)).sort()
    profile.settings.avalon.palette = 'box-art'
    applyThemeProfile(profile, root)
    expect(root.style).toBe(declaration)
    expect(root.querySelector('button')).toBe(control)
    expect(declaration.getPropertyValue('--surface')).toBe('#202429')
    expect(declaration.getPropertyValue('--avalon-flare')).toBe('#FF4D9E')
    expect(declaration.getPropertyValue('--private-view-value')).toBe('retained')
    expect(Array.from({ length: declaration.length }, (_, index) => declaration.item(index)).sort()).toEqual(
      variables,
    )
  })
})

// @vitest-environment jsdom
import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import {
  DEFAULT_TYPOGRAPHY,
  fontFamilyStack,
  parseTypography,
  setAuthoredTypography,
} from '../src/shared/typography'
import {
  DEFAULT_PROFILE,
  parseThemeProfile,
  resolvedTypography,
  selectThemeProfile,
  typographyKey,
  type ThemeProfile,
} from '../src/shared/theme'
import { applyThemeProfile } from '../src/renderer/theming/runtime'
import { AvalonTypographyControls } from '../src/renderer/themes/avalon-typography'
import { migrateAvaloniaPalette } from '../src/renderer/themes/avalon-migration'
import { afterglow } from '../src/renderer/themes/afterglow'
import { avalon } from '../src/renderer/themes/avalon'
import type { WinnowBridge } from '../src/shared/bridge'

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  setAuthoredTypography([])
})
const custom = { headingFont: 'Georgia', interfaceFont: 'Segoe UI', dataFont: 'Consolas', sizePercent: 115 }

describe('Original typography contract', () => {
  it('accepts old profiles and partial typography with original role defaults', () => {
    expect(resolvedTypography(parseThemeProfile(DEFAULT_PROFILE))).toEqual(DEFAULT_TYPOGRAPHY)
    expect(parseTypography({ sizePercent: 110 })).toEqual({ ...DEFAULT_TYPOGRAPHY, sizePercent: 110 })
  })
  it.each([
    ['headingFont', ''],
    ['interfaceFont', 'https://example.test/font.ttf'],
    ['dataFont', 'C:\\font.ttf'],
    ['headingFont', 'Arial, Georgia'],
    ['dataFont', 'font#Family'],
    ['interfaceFont', 'Arial\n'],
  ])('rejects invalid %s family %s and names its field', (field, value) => {
    expect(() => parseTypography({ [field]: value })).toThrow(field)
  })
  it.each([
    { sizePercent: 79 },
    { sizePercent: 121 },
    { sizePercent: 100.5 },
    { headingFont: null },
    { unknownFont: 'Arial' },
  ])('rejects malformed typography %j', (value) => {
    expect(() => parseTypography(value)).toThrow()
    const profile = structuredClone(DEFAULT_PROFILE)
    profile.appearance.typography = { winnow: value as never }
    expect(() => parseThemeProfile(profile)).toThrow()
  })
  it('imports original per-palette typography while retaining valid entries beside a damaged entry', () => {
    const { profile } = migrateAvaloniaPalette([
      { preference: 'Theme', value: 'nightshift' },
      {
        preference: 'Typography',
        value: JSON.stringify({
          winnow: { sizePercent: 'bad' },
          nightshift: custom,
          tungsten: { sizePercent: 90 },
        }),
      },
    ])
    expect(resolvedTypography(profile)).toEqual(custom)
    expect(profile.appearance.typography?.winnow).toBeUndefined()
    expect(profile.appearance.typography?.tungsten).toEqual({ ...DEFAULT_TYPOGRAPHY, sizePercent: 90 })
    const restored = parseThemeProfile(JSON.parse(JSON.stringify(profile)))
    expect(resolvedTypography(restored)).toEqual(custom)
  })
  it.each(['not json', '[]', 'null'])(
    'keeps bundled defaults for malformed original preferences %s',
    (stored) => {
      expect(
        resolvedTypography(migrateAvaloniaPalette([{ preference: 'Typography', value: stored }]).profile),
      ).toEqual(DEFAULT_TYPOGRAPHY)
    },
  )
  it('round trips effective role typography through profiles without changing bundled defaults', () => {
    const profile = structuredClone(DEFAULT_PROFILE)
    profile.appearance.typography = { winnow: custom }
    expect(resolvedTypography(parseThemeProfile(JSON.parse(JSON.stringify(profile))))).toEqual(custom)
    expect(DEFAULT_PROFILE.appearance.typography).toBeUndefined()
    const another = selectThemeProfile(profile, 'afterglow', afterglow)
    expect(resolvedTypography(selectThemeProfile(another, 'avalon', avalon))).toEqual(custom)
  })
  it('falls back by role and resolves bundled family names case-insensitively', () => {
    expect(fontFamilyStack('Winnow missing heading 913872', 'headingFont')).toBe(
      '"Winnow missing heading 913872", "Avalon Display", sans-serif',
    )
    expect(fontFamilyStack('Winnow missing interface 913872', 'interfaceFont')).toContain('"Avalon Body"')
    expect(fontFamilyStack('Winnow missing data 913872', 'dataFont')).toContain('"Avalon Data", monospace')
    expect(fontFamilyStack('ibm plex mono', 'headingFont')).toBe(
      '"Avalon Data", "Avalon Display", sans-serif',
    )
  })
  it('reapplies role fonts and text scale from stable baselines independently of interface geometry', () => {
    const root = document.createElement('div'),
      profile = structuredClone(DEFAULT_PROFILE)
    profile.appearance.scale = 95
    for (const sizePercent of [120, 80, 100, 120]) {
      profile.appearance.typography = { winnow: { ...custom, sizePercent } }
      applyThemeProfile(profile, root)
      expect(root.style.getPropertyValue('--theme-text-scale')).toBe(String(sizePercent / 100))
      expect(root.style.getPropertyValue('--interface-scale')).toBe('0.95')
      expect(root.style.getPropertyValue('--font-display')).toContain('"Georgia"')
      expect(root.style.getPropertyValue('--font-body')).toContain('"Segoe UI"')
      expect(root.style.getPropertyValue('--font-mono')).toContain('"Consolas"')
    }
    applyThemeProfile(selectThemeProfile(profile, 'afterglow', afterglow), root)
    expect(root.style.getPropertyValue('--theme-text-scale')).toBe('1')
    expect(root.style.getPropertyValue('--font-body')).not.toContain('Segoe UI", "Avalon')
  })
})

describe.each(['desktop', 'fullscreen'])('Avalon typography controls in %s', (mode) => {
  function Harness({ initial = structuredClone(DEFAULT_PROFILE) }: { initial?: ThemeProfile }) {
    const [profile, setProfile] = useState<ThemeProfile>(initial)
    return (
      <div data-mode={mode}>
        <AvalonTypographyControls
          profile={profile}
          onChange={(typography) =>
            setProfile((current) => ({ ...current, appearance: { ...current.appearance, typography } }))
          }
        />
        <button
          onClick={() =>
            setProfile((current) => ({
              ...current,
              settings: {
                avalon: { palette: typographyKey(current) === 'winnow' ? 'nightshift' : 'winnow' },
              },
            }))
          }
        >
          Switch palette
        </button>
        <button
          onClick={() => setProfile((current) => parseThemeProfile(JSON.parse(JSON.stringify(current))))}
        >
          Reload profile
        </button>
        <output data-testid="effective">{JSON.stringify(resolvedTypography(profile))}</output>
      </div>
    )
  }
  it('keeps independent role fonts and size through palette switches, restart and reset', () => {
    render(<Harness />)
    fireEvent.change(screen.getByRole('combobox', { name: 'Heading font' }), {
      target: { value: 'IBM Plex Mono' },
    })
    fireEvent.change(screen.getByRole('combobox', { name: 'Interface font' }), {
      target: { value: 'Bricolage Grotesque' },
    })
    fireEvent.change(screen.getByRole('combobox', { name: 'Data font' }), {
      target: { value: 'Plus Jakarta Sans' },
    })
    fireEvent.change(screen.getByRole('slider', { name: 'Theme text size' }), { target: { value: '115' } })
    const expected = JSON.parse(screen.getByTestId('effective').textContent!)
    expect(expected).toEqual({
      headingFont: 'IBM Plex Mono',
      interfaceFont: 'Bricolage Grotesque',
      dataFont: 'Plus Jakarta Sans',
      sizePercent: 115,
    })
    fireEvent.click(screen.getByText('Switch palette'))
    expect(JSON.parse(screen.getByTestId('effective').textContent!)).toEqual(DEFAULT_TYPOGRAPHY)
    fireEvent.change(screen.getByRole('slider', { name: 'Theme text size' }), { target: { value: '90' } })
    fireEvent.click(screen.getByText('Switch palette'))
    fireEvent.click(screen.getByText('Reload profile'))
    expect(JSON.parse(screen.getByTestId('effective').textContent!)).toEqual(expected)
    fireEvent.click(screen.getByRole('button', { name: 'Reset theme typography' }))
    expect(JSON.parse(screen.getByTestId('effective').textContent!)).toEqual(DEFAULT_TYPOGRAPHY)
    fireEvent.click(screen.getByText('Switch palette'))
    expect(JSON.parse(screen.getByTestId('effective').textContent!).sizePercent).toBe(90)
  })
  it('loads installed fonts only on request and keeps an editable family fallback', async () => {
    window.winnow = {
      listFonts: vi.fn(async () => ['Georgia', 'Consolas', 'IBM Plex Mono']),
    } as unknown as WinnowBridge
    render(<Harness />)
    expect(window.winnow.listFonts).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Find installed fonts' }))
    await screen.findByText('3 installed font families available.')
    expect(screen.getAllByRole('option', { name: 'Georgia', hidden: true })).toHaveLength(3)
    fireEvent.change(screen.getByRole('combobox', { name: 'Heading font' }), { target: { value: 'Georgia' } })
    expect(JSON.parse(screen.getByTestId('effective').textContent!).headingFont).toBe('Georgia')
    fireEvent.change(screen.getByRole('combobox', { name: 'Heading font' }), {
      target: { value: 'https://bad.test/font' },
    })
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('single font'))
    expect(JSON.parse(screen.getByTestId('effective').textContent!).headingFont).toBe('Georgia')
  })
  it('shows the canonical bundled choice without rewriting a lowercase saved family', () => {
    const profile = structuredClone(DEFAULT_PROFILE)
    profile.appearance.typography = {
      winnow: { ...DEFAULT_TYPOGRAPHY, headingFont: 'IBM Plex Mono', sizePercent: 115 },
    }
    const onChange = vi.fn()
    const view = render(<AvalonTypographyControls profile={profile} onChange={onChange} />)
    const updated = structuredClone(profile)
    updated.appearance.typography!.winnow.headingFont = 'ibm plex mono'
    view.rerender(<AvalonTypographyControls profile={updated} onChange={onChange} />)
    expect((screen.getByRole('combobox', { name: 'Heading font' }) as HTMLInputElement).value).toBe(
      'IBM Plex Mono',
    )
    expect(resolvedTypography(updated).headingFont).toBe('ibm plex mono')
    expect(onChange).not.toHaveBeenCalled()
  })
  it('resets maximum-size edits to the authored IBM heading and 105 percent', () => {
    const authored = { ...DEFAULT_TYPOGRAPHY, headingFont: 'IBM Plex Mono', sizePercent: 105 }
    setAuthoredTypography([{ id: 'winnow', typography: authored }])
    render(<Harness />)
    fireEvent.change(screen.getByRole('slider', { name: 'Theme text size' }), { target: { value: '120' } })
    expect(JSON.parse(screen.getByTestId('effective').textContent!).sizePercent).toBe(120)
    fireEvent.click(screen.getByRole('button', { name: 'Reset theme typography' }))
    expect(JSON.parse(screen.getByTestId('effective').textContent!)).toEqual(authored)
    expect((screen.getByRole('slider', { name: 'Theme text size' }) as HTMLInputElement).value).toBe('105')
  })
})

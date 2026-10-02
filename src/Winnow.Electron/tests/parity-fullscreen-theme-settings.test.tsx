// @vitest-environment jsdom
import { useState } from 'react'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { FullscreenThemeSettings } from '../src/renderer/features/FullscreenThemeSettings'
import { DEFAULT_PROFILE, resolvedTypography, typographyKey, type ThemeProfile } from '../src/shared/theme'
import { DEFAULT_TYPOGRAPHY } from '../src/shared/typography'
import type { ThemeRuntime } from '../src/renderer/theming/runtime'

afterEach(cleanup)
function fixture(profile = structuredClone(DEFAULT_PROFILE), setup = false) {
  let current = profile
  const openStudio = vi.fn(),
    listFonts = vi.fn(async () => ['Fixture Display', 'Fixture Sans', 'Fixture Mono', 'url:invalid'])
  Object.defineProperty(window, 'winnow', { configurable: true, value: { listFonts } })
  function Harness() {
    const [value, setProfile] = useState(profile)
    current = value
    return (
      <FullscreenThemeSettings
        runtime={{ profile: value, setProfile, theme: { name: 'Avalon' } } as unknown as ThemeRuntime}
        openStudio={openStudio}
        setup={setup}
      />
    )
  }
  return { Harness, profile: () => current, listFonts, openStudio }
}

it('selects the shared palette without overwriting other composition settings and restores its origin', async () => {
  const initial = structuredClone(DEFAULT_PROFILE)
  initial.settings.avalon = { palette: 'winnow', dimCovers: false }
  const f = fixture(initial)
  render(<f.Harness />)
  const theme = screen.getByRole('button', { name: 'Theme' })
  theme.focus()
  fireEvent.click(theme)
  const dialog = screen.getByRole('dialog', { name: 'Theme' })
  fireEvent.click(within(dialog).getByRole('button', { name: 'Nightshift' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  expect(f.profile().settings.avalon).toEqual({ palette: 'nightshift', dimCovers: false })
  await waitFor(() => expect(document.activeElement).toBe(theme))
  fireEvent.click(screen.getByRole('button', { name: 'Theme Studio' }))
  expect(f.openStudio).toHaveBeenCalledOnce()
})

it.each([false, true])(
  'shows picker-local controller hints that follow font input focus (setup=%s)',
  async (setup) => {
    const f = fixture(structuredClone(DEFAULT_PROFILE), setup)
    render(<f.Harness />)
    const theme = screen.getByRole('button', { name: 'Theme' })
    act(() => theme.focus())
    fireEvent.click(theme)
    const palette = screen.getByRole('dialog', { name: 'Theme' })
    const paletteHints = within(palette).getByRole('group', { name: 'Theme picker controls' })
    expect(paletteHints.textContent).toContain('A Select')
    expect(paletteHints.textContent).toContain('B Back')
    expect(paletteHints.textContent).not.toContain('Keyboard')
    expect(paletteHints.querySelector('[data-theme-picker-glyph="A"] svg')).toBeTruthy()
    expect(paletteHints.querySelector('[data-theme-picker-glyph="B"] svg')).toBeTruthy()
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    await waitFor(() => expect(document.activeElement).toBe(theme))

    const origin = screen.getByRole('button', { name: 'Interface font' })
    act(() => origin.focus())
    fireEvent.click(origin)
    const picker = screen.getByRole('dialog', { name: 'Interface font' })
    const hints = within(picker).getByRole('group', { name: 'Theme picker controls' })
    expect(hints.textContent).not.toContain('Keyboard')
    const input = within(picker).getByRole('textbox', { name: 'Font family' })
    act(() => input.focus())
    expect(hints.textContent).toContain('Y Keyboard')
    expect(hints.querySelector('[data-theme-picker-glyph="Y"] svg')).toBeTruthy()
    act(() => within(picker).getByRole('button', { name: 'Back' }).focus())
    expect(hints.textContent).not.toContain('Keyboard')
    expect(hints.querySelector('[data-theme-picker-glyph="Y"]')).toBeNull()
    act(() => input.focus())
    expect(hints.textContent).toContain('Y Keyboard')
    fireEvent.keyDown(input, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(document.activeElement).toBe(origin))
    fireEvent.click(origin)
    expect(screen.getByRole('group', { name: 'Theme picker controls' }).textContent).not.toContain('Keyboard')
  },
)

it.each([
  ['Heading font', 'headingFont'],
  ['Interface font', 'interfaceFont'],
  ['Data font', 'dataFont'],
] as const)(
  'applies an installed %s to the current palette and preserves the other roles',
  async (label, role) => {
    const f = fixture()
    render(<f.Harness />)
    const row = screen.getByRole('button', { name: label })
    row.focus()
    fireEvent.click(row)
    const dialog = screen.getByRole('dialog', { name: label })
    fireEvent.click(await within(dialog).findByRole('button', { name: 'Fixture Sans' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(resolvedTypography(f.profile())).toEqual({ ...DEFAULT_TYPOGRAPHY, [role]: 'Fixture Sans' })
    expect(f.listFonts).toHaveBeenCalledOnce()
    expect(document.activeElement).toBe(row)
  },
)

it('does not accept CSS or font URL syntax and keeps manual font entry available after enumeration failure', async () => {
  const f = fixture()
  f.listFonts.mockRejectedValue(new Error('private fixture error'))
  render(<f.Harness />)
  fireEvent.click(screen.getByRole('button', { name: 'Heading font' }))
  const dialog = screen.getByRole('dialog', { name: 'Heading font' })
  await within(dialog).findByRole('status')
  expect(document.body.textContent).not.toContain('private fixture error')
  const input = within(dialog).getByLabelText('Font family')
  fireEvent.change(input, { target: { value: 'url:https://example.test/font.woff' } })
  expect((within(dialog).getByRole('button', { name: 'Use font' }) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.change(input, { target: { value: 'Custom Family' } })
  fireEvent.click(within(dialog).getByRole('button', { name: 'Use font' }))
  expect(resolvedTypography(f.profile()).headingFont).toBe('Custom Family')
})

it('bounds theme text size in five-point steps and resets only the current palette typography', () => {
  const initial = structuredClone(DEFAULT_PROFILE)
  initial.appearance.typography = {
    [typographyKey(initial)]: { ...DEFAULT_TYPOGRAPHY, headingFont: 'Custom Family', sizePercent: 115 },
    nightshift: { ...DEFAULT_TYPOGRAPHY, sizePercent: 90 },
  }
  const f = fixture(initial)
  render(<f.Harness />)
  const row = screen.getByRole('button', { name: 'Theme text size' })
  fireEvent.keyDown(row, { key: 'ArrowRight' })
  expect(resolvedTypography(f.profile()).sizePercent).toBe(120)
  fireEvent.keyDown(row, { key: 'ArrowRight' })
  expect(resolvedTypography(f.profile()).sizePercent).toBe(120)
  for (let i = 0; i < 10; i++) fireEvent.keyDown(row, { key: 'ArrowLeft' })
  expect(resolvedTypography(f.profile()).sizePercent).toBe(80)
  fireEvent.click(screen.getByRole('button', { name: 'Reset theme typography' }))
  expect(resolvedTypography(f.profile())).toEqual(DEFAULT_TYPOGRAPHY)
  expect(f.profile().appearance.typography?.nightshift.sizePercent).toBe(90)
  expect(f.profile().settings).toEqual(initial.settings)
})

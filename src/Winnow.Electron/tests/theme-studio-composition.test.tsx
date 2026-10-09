// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { WinnowBridge } from '../src/shared/bridge'
import { DEFAULT_PROFILE, PALETTES, type ThemeDefinition, type ThemeProfile } from '../src/shared/theme'
import { ThemeStudio } from '../src/renderer/theming/ThemeStudio'
import { useThemeRuntime } from '../src/renderer/theming/runtime'
import { AVALON_PALETTES, registerAvalonThemes } from '../src/renderer/themes/avalon-palettes'
import { chooseOption } from './choice-test-utils'

vi.mock('../src/renderer/api/hooks', () => ({ useLibrary: () => ({ data: undefined }) }))
vi.mock('../src/renderer/components/portal-effects', () => ({ PortalSurface: () => null }))

const builtins: ThemeDefinition[] = [
  {
    apiVersion: 1,
    id: 'avalon',
    name: 'Avalon',
    settings: [
      {
        id: 'palette',
        label: 'Avalon palette',
        type: 'select',
        default: 'winnow',
        options: [
          { value: 'winnow', label: 'Winnow' },
          { value: 'profile', label: 'Studio colors' },
        ],
      },
    ],
  },
  ...['Afterglow', 'Rift', 'Catalogue'].map((name): ThemeDefinition => ({
    apiVersion: 1,
    id: name.toLowerCase(),
    name,
  })),
]
let profile: ThemeProfile
function selectComposition(name: string) {
  fireEvent.click(screen.getByText(name, { selector: '.studio-theme-label strong' }).closest('button')!)
}
function Studio({ mode }: { mode: string }) {
  const runtime = useThemeRuntime(builtins)
  return <div className={mode}>{!runtime.loading && <ThemeStudio runtime={runtime} />}</div>
}
beforeEach(() => {
  profile = structuredClone(DEFAULT_PROFILE)
  window.winnow = {
    loadPreferences: vi.fn(async () => profile),
    savePreferences: vi.fn(async () => {}),
    listThemes: vi.fn(async () => []),
  } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  registerAvalonThemes([])
  vi.restoreAllMocks()
})

describe.each(['desktop', 'fullscreen'])('Composition appearance in %s', (mode) => {
  it('shows only applicable controls as the composition changes', async () => {
    render(<Studio mode={mode} />)
    await screen.findByRole('heading', { name: 'Atmosphere' })
    for (const [name, visible] of [
      ['Avalon', []],
      ['Afterglow', ['Typography', 'Spacing', 'Navigation', 'Library cards', 'Game details']],
      ['Rift', ['Typography', 'Cover finish', 'Game details']],
      ['Catalogue', ['Typography', 'Cover finish', 'Library cards', 'Game details']],
    ] as const) {
      selectComposition(name)
      const choices = within(screen.getByRole('region', { name: 'Atmosphere' })).getByRole('group', {
        name: 'Palette',
      })
      expect(within(choices).getAllByRole('button').length).toBe(name === 'Avalon' ? 10 : 5)
      expect(screen.queryByRole('button', { name: 'Avalon palette' })).toBeNull()
      for (const control of [
        'Typography',
        'Spacing',
        'Cover finish',
        'Navigation',
        'Library cards',
        'Game details',
      ]) {
        expect(!!screen.queryByRole('button', { name: control }), `${name}: ${control}`).toBe(
          (visible as readonly string[]).includes(control),
        )
      }
      expect(!!screen.queryByRole('textbox', { name: 'Accent color' })).toBe(name !== 'Avalon')
      expect(!!screen.queryByText('Customize every color')).toBe(name !== 'Avalon')
      expect(!!screen.queryByRole('slider', { name: /Artwork shade/ })).toBe(name === 'Afterglow')
      expect(!!screen.queryByRole('group', { name: 'Discover sections' })).toBe(name === 'Afterglow')
      expect(!!screen.queryByRole('heading', { name: 'Artwork materials' })).toBe(
        name === 'Rift' || name === 'Catalogue',
      )
      expect(!!screen.queryByRole('heading', { name: 'Arrange your space' })).toBe(name !== 'Avalon')
      expect(screen.getByRole('slider', { name: /Corners/ })).toBeTruthy()
      expect(screen.getByRole('slider', { name: /Interface size/ })).toBeTruthy()
      expect(screen.getByRole('checkbox', { name: /^Reduce motion/ })).toBeTruthy()
    }
  })

  it('keeps saved values when controls disappear and restores them on return', async () => {
    profile.themeId = 'catalogue'
    render(<Studio mode={mode} />)
    chooseOption(await screen.findByRole('button', { name: 'Cover finish' }), 'foil')
    chooseOption(screen.getByRole('button', { name: 'Game details' }), 'stacked')
    selectComposition('Avalon')
    expect(screen.queryByRole('button', { name: 'Cover finish' })).toBeNull()
    await waitFor(() =>
      expect(window.winnow.savePreferences).toHaveBeenLastCalledWith(
        expect.objectContaining({
          themeId: 'avalon',
          designs: expect.objectContaining({
            catalogue: expect.objectContaining({
              appearance: expect.objectContaining({ artwork: expect.objectContaining({ finish: 'foil' }) }),
              layout: expect.objectContaining({ detailArrangement: 'stacked' }),
            }),
          }),
        }),
      ),
    )
    selectComposition('Catalogue')
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Cover finish' }).value).toBe('foil')
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Game details' }).value).toBe('stacked')
  })

  it('previews the original palette colors and remembers its selection across composition switches', async () => {
    render(<Studio mode={mode} />)
    const palette = within(await screen.findByRole('group', { name: 'Palette' }))
    for (const item of AVALON_PALETTES) {
      const swatch = palette.getByRole('button', { name: item.name })
      expect([...swatch.querySelectorAll('i')].map((color) => color.style.background)).toEqual(
        ['--bg', '--surface', '--text', '--accent'].map((key) => {
          const probe = document.createElement('i')
          probe.style.background = item.colors[key]
          return probe.style.background
        }),
      )
    }
    fireEvent.click(palette.getByRole('button', { name: 'Nightshift' }))
    expect(palette.getByRole('button', { name: 'Nightshift' }).getAttribute('aria-pressed')).toBe('true')
    selectComposition('Afterglow')
    selectComposition('Avalon')
    expect(screen.getByRole('button', { name: 'Nightshift' }).getAttribute('aria-pressed')).toBe('true')
    await waitFor(() =>
      expect(window.winnow.savePreferences).toHaveBeenLastCalledWith(
        expect.objectContaining({ settings: expect.objectContaining({ avalon: { palette: 'nightshift' } }) }),
      ),
    )
  })

  it('shows Avalon custom colors only when Studio colors is selected', async () => {
    render(<Studio mode={mode} />)
    const palette = within(await screen.findByRole('group', { name: 'Palette' }))
    expect(screen.queryByRole('textbox', { name: 'Accent color' })).toBeNull()
    fireEvent.click(palette.getByRole('button', { name: 'Studio colors' }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Accent color' }), { target: { value: '#123456' } })
    fireEvent.click(palette.getByRole('button', { name: 'Winnow' }))
    expect(screen.queryByRole('textbox', { name: 'Accent color' })).toBeNull()
    fireEvent.click(palette.getByRole('button', { name: 'Studio colors' }))
    expect(screen.getByRole<HTMLInputElement>('textbox', { name: 'Accent color' }).value).toBe('#123456')
    const presets = within(screen.getByRole('group', { name: 'Studio color preset' }))
    fireEvent.click(presets.getByRole('button', { name: 'Paper trail' }))
    expect(screen.getByRole<HTMLInputElement>('textbox', { name: 'Accent color' }).value).toBe(
      PALETTES.paper.accent,
    )
    expect(palette.getByRole('button', { name: 'Studio colors' }).getAttribute('aria-pressed')).toBe('true')
  })
})

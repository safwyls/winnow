import { expect, it, vi } from 'vitest'
import { AVALON_PALETTES } from '../src/renderer/themes/avalon-palettes'
import { avalonAppearance, avalonSurfaceTokens, luminance } from '../src/renderer/themes/avalon-translucency'
import { originalRoles } from './helpers/original-theme-tokens'
import { SessionAppearance } from '../src/main/appearance-session'

it.each(AVALON_PALETTES)(
  'only $id ground and caption move with layout at every whole slider position and reach',
  (palette) => {
    for (let percent = 0; percent <= 100; percent++)
      for (const wall of [false, true]) {
        const flush = originalRoles(palette, percent, wall, 'flush')
        const floating = originalRoles(palette, percent, wall, 'floating')
        expect(Object.keys(flush)).toHaveLength(61)
        expect(Object.keys(floating)).toEqual(Object.keys(flush))
        for (const [key, value] of Object.entries(flush)) {
          if (key === 'ShellGround' || key === 'CaptionFill') continue
          expect(floating[key], `${palette.id}/${percent}/${wall}/${key}`).toBe(value)
        }
      }
  },
)

it.each(AVALON_PALETTES)(
  '$id fields are exactly one opaque palette step above their pane in both layouts',
  (palette) => {
    for (const layout of ['floating', 'flush'] as const) {
      const tokens = avalonSurfaceTokens(palette, 0, true, layout)
      expect(tokens.PaneGround).toBe(`${palette.colors['--bg']}FF`)
      expect(tokens.ChromeFieldOnGround).toBe(`${palette.colors['--surface']}FF`)
      expect(luminance(tokens.ChromeFieldOnGround)).toBeGreaterThan(luminance(tokens.PaneGround))
    }
  },
)

it('reads original and unknown stored layout IDs with floating as the unset default', () => {
  for (const [stored, expected] of [
    ['flush', 'flush'],
    ['floating', 'floating'],
    ['islands', 'floating'],
    ['tiles-2029', 'floating'],
    [null, 'floating'],
  ] as const)
    expect(avalonAppearance({ Layout: stored }, AVALON_PALETTES[0], true).layout).toBe(expected)
})

it('seals the exact Tungsten 60 Mica content-pane session through Flush then Floating without writes', async () => {
  const session = new SessionAppearance({
    palette: 'tungsten',
    transparency: 60,
    backdrop: 'mica',
    wallTranslucent: true,
    layout: 'floating',
  })
  const persisted = vi.fn(async () => ({ ok: true, status: 204 }))
  for (const value of ['flush', 'floating'])
    await session.request(
      { route: 'preferences.presentation.put', params: { preference: 'Layout' }, body: { value } },
      persisted,
    )
  expect(persisted).not.toHaveBeenCalled()
  const result = await session.request({ route: 'preferences.presentation.get' }, async () => ({
    ok: true,
    status: 200,
    data: [],
  }))
  const values = Object.fromEntries(
    (result.data as { preference: string; value: string }[]).map(({ preference, value }) => [
      preference,
      value,
    ]),
  )
  expect(
    avalonAppearance(
      values,
      AVALON_PALETTES.find(({ id }) => id === 'tungsten')!,
      true,
    ),
  ).toEqual({ transparency: 60, backdrop: 'mica', wallTranslucent: true, layout: 'floating' })
})

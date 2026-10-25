import { expect, it, vi } from 'vitest'
import { appearanceSession } from '../src/shared/appearance-session'
import { SessionAppearance } from '../src/main/appearance-session'
import { DEFAULT_PROFILE } from '../src/shared/theme'

it('supports every original development capture switch and preserves exact default and first-occurrence semantics', () => {
  expect(appearanceSession([], false)).toBeNull()
  expect(appearanceSession(['--theme', 'tungsten', '--Transparent'], false)).toBeNull()
  expect(appearanceSession(['--theme=tungsten'], true)).toBeNull()
  expect(appearanceSession(['--theme=tungsten', '--theme=box-art'], false)).toEqual({
    palette: 'tungsten',
    transparency: 0,
    backdrop: 'acrylic',
    wallTranslucent: true,
    layout: 'floating',
  })
  expect(
    appearanceSession(['--transparency=60', '--backdrop=mica', '--wall=off', '--layout=flush'], false),
  ).toEqual({ palette: null, transparency: 60, backdrop: 'mica', wallTranslucent: false, layout: 'flush' })
  expect(appearanceSession(['--transparent', '--transparency=12'], false)?.transparency).toBe(12)
  expect(appearanceSession(['--theme=', '--backdrop=Mica', '--wall=TRUE', '--layout=other'], false)).toEqual({
    palette: '',
    transparency: 0,
    backdrop: 'acrylic',
    wallTranslucent: false,
    layout: 'floating',
  })
})
it.each([
  ['-5', 0],
  ['240', 100],
  ['  +55 ', 55],
  ['1.5', 100],
  ['bad', 100],
  ['2147483648', 100],
  ['-2147483649', 100],
])('parses the original int32 override %s without broadening its syntax', (value, expected) => {
  expect(appearanceSession([`--transparency=${value}`, '--transparent'], false)?.transparency).toBe(expected)
})
it.each(['on', 'true', '1', 'off', 'false', '0', 'True', ''])(
  'preserves the original wall switch value %s',
  (value) => {
    expect(appearanceSession([`--wall=${value}`], false)?.wallTranslucent).toBe(
      ['on', 'true', '1'].includes(value),
    )
  },
)
it('all appearance edits stay live in memory while unrelated preferences retain normal persistence', async () => {
  const session = new SessionAppearance(appearanceSession(['--theme=tungsten', '--transparency=60'], false)!)
  const write = vi.fn(async () => ({ ok: true, status: 204 }))
  const edits: [string, string][] = [
    ['Theme', 'box-art'],
    ['Transparency', '12'],
    ['Transparency', '0'],
    ['Backdrop', 'mica'],
    ['TranslucentWall', 'true'],
    ['TranslucentWall', 'false'],
    ['Layout', 'flush'],
    ['Typography', '{"box-art":{"sizePercent":120}}'],
  ]
  for (const [preference, value] of edits)
    expect(
      await session.request(
        { route: 'preferences.presentation.put', params: { preference }, body: { value } },
        write,
      ),
    ).toEqual({ ok: true, status: 204 })
  expect(write).not.toHaveBeenCalled()
  const response = await session.request({ route: 'preferences.presentation.get' }, async () => ({
    ok: true,
    status: 200,
    data: [
      { preference: 'Theme', value: 'rose-pine' },
      { preference: 'LibrarySort', value: 'title' },
    ],
  }))
  expect(
    Object.fromEntries(
      (response.data as { preference: string; value: string }[]).map((row) => [row.preference, row.value]),
    ),
  ).toMatchObject({
    Theme: 'box-art',
    Transparency: '0',
    Backdrop: 'mica',
    TranslucentWall: 'false',
    Layout: 'flush',
    LibrarySort: 'title',
  })
  await session.request(
    {
      route: 'preferences.presentation.put',
      params: { preference: 'LibrarySort' },
      body: { value: 'lastPlayed' },
    },
    write,
  )
  expect(write).toHaveBeenCalledOnce()
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.settings.avalon = { palette: 'box-art' }
  session.saveProfile(profile)
  profile.settings.avalon.palette = 'nightshift'
  expect(session.loadProfile()).toMatchObject({ settings: { avalon: { palette: 'box-art' } } })
  expect(new SessionAppearance(session.options).loadProfile()).toBeNull()
})
it('does not hide unrelated request failures or accept malformed appearance values', async () => {
  const session = new SessionAppearance(appearanceSession(['--theme=winnow'], false)!)
  const next = vi.fn(async () => ({ ok: false, status: 503, message: 'Offline' }))
  expect(await session.request({ route: 'preferences.presentation.get' }, next)).toMatchObject({
    ok: false,
    status: 503,
  })
  expect(
    await session.request(
      { route: 'preferences.presentation.put', params: { preference: 'Transparency' }, body: { value: [] } },
      next,
    ),
  ).toMatchObject({ ok: false, status: 400 })
  expect(next).toHaveBeenCalledOnce()
  expect(() => session.saveProfile([])).toThrow()
})

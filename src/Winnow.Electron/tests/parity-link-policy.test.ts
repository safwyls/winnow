import { describe, expect, it, vi } from 'vitest'
import { routeLink } from '../src/main/link-routing'
import { readableWebUrl, validateExternalUrl } from '../src/main/security'
import { BrowserController, browserControllerKey, readBrowserPad } from '../src/main/browser-controller'

describe('GameLinkRouter reading and fallback parity', () => {
  it.each([
    ['in-app', true, false, 'https://store.steampowered.com/news/app/440', 'embedded', false],
    ['browser', true, true, 'https://store.steampowered.com/news/app/440', 'https', false],
    ['store', true, true, 'https://store.steampowered.com/app/440/', 'steam', false],
    ['store', true, false, 'https://store.steampowered.com/app/440/', 'https', true],
    ['store', true, true, 'https://store.steampowered.com/news/app/440', 'https', true],
    ['in-app', false, true, 'https://store.steampowered.com/news/app/440', 'https', true],
    ['in-app', true, true, 'https://example.com/news', 'embedded', false],
    ['in-app', true, true, 'https://store.steampowered.com/app/440/', 'embedded', false],
    ['in-app', true, true, 'https://www.gog.com/en/game/hades', 'embedded', false],
    ['in-app', true, true, 'https://store.epicgames.com/en-US/p/hades', 'embedded', false],
    ['in-app', true, true, 'https://www.steamgriddb.com/grid/1', 'embedded', false],
    ['in-app', true, true, 'http://example.com/article', 'embedded', false],
    ['store', true, true, 'https://example.com/article', 'https', true],
    ['store', true, true, 'https://www.steamgriddb.com/grid/1', 'https', true],
    ['store', true, true, 'http://example.com/article', 'http', true],
    ['in-app', true, true, 'goggalaxy://openGameView/gog_1', 'goggalaxy', false],
    ['browser', true, false, 'goggalaxy://openGameView/gog_1', 'goggalaxy', false],
    ['in-app', true, false, 'com.epicgames.launcher://store/library', 'com.epicgames.launcher', false],
  ])(
    'routes %s / %s / %s / %s to %s with fallback %s',
    async (preference, embedded, client, url, scheme, fallback) => {
      const inApp = vi.fn(async () => Boolean(embedded)),
        external = vi.fn(async (_url: string) => true)
      const result = await routeLink(String(url), String(preference), {
        inApp,
        external,
        hasSteam: () => Boolean(client),
      })
      expect(result.opened).toBe(true)
      expect(!!result.message).toBe(fallback)
      if (scheme === 'embedded') {
        expect(inApp).toHaveBeenCalledOnce()
        expect(external).not.toHaveBeenCalled()
      } else expect(new URL(external.mock.calls[0][0] as string).protocol).toBe(`${scheme}:`)
    },
  )
  it.each([false, true])('reports browser fallback after store refusal or throw=%s', async (throws) => {
    const calls: string[] = []
    const result = await routeLink('https://store.steampowered.com/app/440/', 'store', {
      inApp: vi.fn(),
      hasSteam: () => true,
      external: async (url) => {
        calls.push(url)
        if (url.startsWith('steam:')) {
          if (throws) throw Error('gone')
          return false
        }
        return true
      },
    })
    expect(calls).toEqual(['steam://store/440', 'https://store.steampowered.com/app/440/'])
    expect(result).toMatchObject({ opened: true, message: expect.stringContaining('Opened in your browser') })
  })
  it.each([false, true])('retains fallback after reader refusal or throw=%s', async (throws) => {
    const external = vi.fn(async () => true)
    const result = await routeLink('https://example.com/', 'in-app', {
      inApp: async () => {
        if (throws) throw Error('reader unavailable')
        return false
      },
      external,
      hasSteam: () => false,
    })
    expect(result).toMatchObject({ opened: true, message: expect.stringContaining('Opened in your browser') })
    expect(external).toHaveBeenCalledWith('https://example.com/')
  })
  it.each([false, true])(
    'never claims a fallback succeeded when the browser refuses or throws=%s',
    async (throws) => {
      const result = await routeLink('https://example.com/', 'store', {
        inApp: vi.fn(),
        hasSteam: () => false,
        external: async () => {
          if (throws) throw Error('missing browser')
          return false
        },
      })
      expect(result.opened).toBe(false)
      expect(result.message).not.toContain('Opened')
    },
  )
})

describe('page-controlled reading boundary', () => {
  it.each([
    'https://store.steampowered.com/app/440/',
    'https://store.epicgames.com/en-US/p/hades',
    'https://www.gog.com/en/game/stardew_valley',
    'https://steamcommunity.com/games/413150/announcements/detail/1',
    'https://steamstore-a.akamaihd.net/news/externalpost/1',
    'https://example.com/',
    'http://example.com:8080/article?q=hello#section',
    'HTTPS://EXAMPLE.COM/',
    'https://www.youtube.com/embed/1',
  ])('accepts original readable web fixture %s', (address) =>
    expect(readableWebUrl(address)).toBe(new URL(address).href),
  )
  it.each([
    'javascript:alert(1)',
    'data:text/html;base64,PHNjcmlwdD4=',
    'file:///C:/Windows/System32/cmd.exe',
    'steam://run/440',
    'mailto:test@example.com',
    'ms-msdt:/id',
    'blob:https://example.com/1234',
    'about:blank',
    'about:settings',
    '/news/app/440',
    '',
    null,
    'winnow-app://app/index.html',
    'winnow-browser://external',
    'goggalaxy://opengameview/gog_1',
    'http://127.0.0.1:12345/',
    'http://localhost.:12345/',
    'http://sub.localhost.:12345/',
    'http://[::ffff:127.0.0.1]:12345/',
    'http://[::]:12345/',
    'https://user:pass@example.com/',
  ])('rejects non-reading destination %s', (address) => expect(readableWebUrl(address)).toBeNull())
  it('permits only explicit safe launcher views and refuses executable actions or appended commands', () => {
    expect(validateExternalUrl('goggalaxy://openGameView/gog_1971477531')).toBe(
      'goggalaxy://opengameview/gog_1971477531',
    )
    expect(validateExternalUrl('com.epicgames.launcher://store/library')).toBe(
      'com.epicgames.launcher://store/library',
    )
    for (const address of [
      'steam://run/1',
      'steam://install/1',
      'steam://uninstall/1',
      'goggalaxy://launchGame/gog_1',
      'goggalaxy://installationScreen/1',
      'goggalaxy://opengameview/gog_1?launch=true',
      'goggalaxy://opengameview/gog_1/extra',
      'goggalaxy://opengameview/gog_1%0a',
      'com.epicgames.launcher://apps/a%3Ab%3Ac?action=launch',
      'com.epicgames.launcher://store/library?cmd=run',
      'com.epicgames.launcher.evil://store/library',
      'winnow-plugin://action',
    ])
      expect(() => validateExternalUrl(address)).toThrow()
  })
})

describe('reading browser controller input parity', () => {
  it.each([
    [12, false, 'Tab', true],
    [13, false, 'Tab', false],
    [12, true, 'Up', false],
    [13, true, 'Down', false],
    [0, false, 'Enter', false],
    [2, false, 'Space', false],
    [8, false, 'Backspace', false],
    [7, true, 'PageDown', false],
    [4, true, 'Tab', true],
  ] as const)('maps button %s reading=%s to %s with shift=%s', (button, reading, key, shift) =>
    expect(browserControllerKey(button, reading)).toEqual({ key, shift }),
  )
  it('never forwards window or composer actions to the page', () => {
    for (const button of [1, 3, 9]) expect(browserControllerKey(button, false)).toBeNull()
    expect(browserControllerKey(8, true)).toBeNull()
  })
  it('requires neutral after focus or connection changes and repeats only directional reading inputs', () => {
    const controller = new BrowserController(),
      sample = (buttons: number[], index = 0) => ({
        index,
        buttons: Array.from({ length: 18 }, (_, i) => buttons.includes(i)),
        axes: [0, 0],
      })
    expect(controller.sample(sample([1]), true, 0)).toEqual([])
    expect(controller.sample(sample([]), true, 10)).toEqual([])
    expect(controller.sample(sample([13]), true, 20)).toEqual([13])
    expect(controller.sample(sample([13]), true, 419)).toEqual([])
    expect(controller.sample(sample([13]), true, 420)).toEqual([13])
    controller.sample(sample([]), false, 430)
    expect(controller.sample(sample([0]), true, 440)).toEqual([])
    controller.sample(sample([]), true, 450)
    expect(controller.sample(sample([0]), true, 460)).toEqual([0])
    expect(controller.sample(sample([0]), true, 1000)).toEqual([])
    expect(controller.sample(sample([1], 1), true, 1100)).toEqual([])
  })
  it('refuses malformed samples and does not interpret a deflected stick as neutral', () => {
    for (const value of [
      { index: 0, buttons: [1], axes: [] },
      { index: 0, buttons: [], axes: [Infinity] },
      { index: -1, buttons: [], axes: [] },
      null,
    ])
      expect(readBrowserPad(value)).toBeNull()
    const controller = new BrowserController()
    expect(controller.sample({ index: 0, buttons: [], axes: [1, 0] }, true, 0)).toEqual([])
    expect(controller.sample({ index: 0, buttons: [], axes: [0, 0] }, true, 1)).toEqual([])
    expect(controller.sample({ index: 0, buttons: [], axes: [1, 0] }, true, 2)).toEqual([15])
  })
})

import { describe, expect, it, vi } from 'vitest'
import { routeLink, steamStoreTarget } from '../src/main/link-routing'

describe('native reading destinations', () => {
  it.each(['https://store.steampowered.com/app/10/', 'https://store.steampowered.com/app/10/Name/'])(
    'routes exact app pages to Steam: %s',
    (value) => {
      expect(steamStoreTarget(value)).toBe('steam://store/10')
    },
  )
  it.each([
    'http://store.steampowered.com/app/440/',
    'https://store.steampowered.com/app/440%2f123/',
    'https://store.steampowered.com/account/',
    'https://store.steampowered.com/app/10/?key=value',
    'https://store.steampowered.com/app/10/#x',
    'https://store.steampowered.com:444/app/10/',
    'https://store.steampowered.com.evil.test/app/10/',
    'https://user@store.steampowered.com/app/10/',
    'https://store.steampowered.com/app/4294967296/',
    'https://store.steampowered.com/app/0/',
  ])('keeps other pages out of the native store: %s', (value) => {
    expect(steamStoreTarget(value)).toBeNull()
  })
  it('defaults to the in-app reader and falls back to the browser on failure', async () => {
    const adapters = {
      inApp: vi.fn().mockRejectedValue(Error('unavailable')),
      external: vi.fn(),
      hasSteam: () => false,
    }
    await routeLink('https://example.com/article', undefined, adapters)
    expect(adapters.inApp).toHaveBeenCalledWith('https://example.com/article')
    expect(adapters.external).toHaveBeenCalledWith('https://example.com/article')
  })
  it('uses store preference only with a client, falls back if dispatch fails, and leaves explicit native links intact', async () => {
    const adapters = {
      inApp: vi.fn(),
      external: vi.fn().mockRejectedValueOnce(Error('client gone')).mockResolvedValue(undefined),
      hasSteam: () => true,
    }
    await routeLink('https://store.steampowered.com/app/10/', 'store', adapters)
    expect(adapters.external.mock.calls).toEqual([
      ['steam://store/10'],
      ['https://store.steampowered.com/app/10/'],
    ])
    expect(adapters.inApp).not.toHaveBeenCalled()
    await routeLink('steam://store/20', 'in-app', adapters)
    expect(adapters.external).toHaveBeenLastCalledWith('steam://store/20')
  })
  it('honors browser preference and validates links before dispatch', async () => {
    const adapters = { inApp: vi.fn(), external: vi.fn(), hasSteam: () => true }
    await routeLink('https://example.com/', 'browser', adapters)
    expect(adapters.inApp).not.toHaveBeenCalled()
    await expect(routeLink('file:///C:/private', 'browser', adapters)).resolves.toEqual({
      opened: false,
      message: 'This link is unavailable.',
    })
    expect(adapters.external).toHaveBeenCalledTimes(1)
  })
})

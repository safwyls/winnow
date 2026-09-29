import { describe, it, expect } from 'vitest'
import { readSteamIdentity, steamMintAllowed, steamNavigationAllowed } from '../src/main/steam-auth-policy'
import { resolveRoute } from '../src/main/routes'
const steamId = '76561198000000001'
const token = (claims: unknown) => `x.${Buffer.from(JSON.stringify(claims)).toString('base64url')}.x`
describe('Steam sign-in boundary migrated from WebView identity policy', () => {
  it('displays login pages without allowing a token probe in their documents', () => {
    for (const path of [
      'login/',
      'join/',
      'password/recover',
      'twofactor/',
      'mobilelogin/',
      'account/security',
    ]) {
      const url = `https://store.steampowered.com/${path}`
      expect(steamNavigationAllowed(url)).toBe(true)
      expect(steamMintAllowed(url)).toBe(false)
    }
    expect(steamMintAllowed('https://store.steampowered.com/')).toBe(true)
    expect(steamMintAllowed('https://store.steampowered.com/explore/')).toBe(true)
    expect(steamMintAllowed('https://steamcommunity.com/')).toBe(false)
  })
  it('restricts top-level navigation to exact Steam HTTPS origins', () => {
    expect(steamNavigationAllowed('https://store.steampowered.com/login/')).toBe(true)
    for (const url of [
      'https://store.steampowered.com.evil.test/',
      'http://store.steampowered.com/',
      'file:///x',
      'https://user@store.steampowered.com/',
      'https://store.steampowered.com:8443/',
    ])
      expect(steamNavigationAllowed(url)).toBe(false)
  })
  it('binds page identity to an unexpired token and refuses disagreements', () => {
    const credential = token({ sub: steamId, exp: 2000 })
    expect(readSteamIdentity({ steamid: steamId, token: credential }, 1000_000)).toEqual({
      steamId,
      token: credential,
    })
    expect(readSteamIdentity({ token: credential }, 1000_000)?.steamId).toBe(steamId)
    expect(() => readSteamIdentity({ steamid: '76561198000000002', token: credential }, 1000_000)).toThrow(
      'different account',
    )
    expect(readSteamIdentity({ token: credential }, 2000_000)).toBeNull()
    expect(readSteamIdentity({ token: token({ sub: 'bad', exp: 2000 }) }, 1000_000)).toBeNull()
    expect(readSteamIdentity({ token: 'bad' })).toBeNull()
  })
  it('only permits larger payloads for named upload operations', () => {
    const content = 'a'.repeat(3 * 1024 * 1024)
    expect(() => resolveRoute({ route: 'metadata.put', params: { workId: 1 }, body: { content } })).toThrow(
      'too large',
    )
    expect(
      resolveRoute({ route: 'metadata.art-upload', params: { workId: 1 }, body: { content } }).path,
    ).toBe('/api/v1/games/1/metadata/art-upload')
    expect(() => resolveRoute({ route: 'metadata.candidate', params: { igdbId: '../x' } })).toThrow()
    expect(
      resolveRoute({ route: 'plugins.removeSecret', params: { pluginId: 'steamgriddb', key: 'api-key' } })
        .method,
    ).toBe('DELETE')
  })
})

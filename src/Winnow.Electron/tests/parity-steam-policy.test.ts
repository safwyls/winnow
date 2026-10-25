import { describe, expect, it } from 'vitest'
import { runInNewContext } from 'node:vm'
import {
  readSteamIdentity,
  steamMintAllowed,
  steamNavigationAllowed,
  steamTokenProbe,
  steamIdentitiesAgree,
} from '../src/main/steam-auth-policy'
import { steamCapturePage } from '../src/main/steam-capture'

const root = 'https://store.steampowered.com/'
const mintPages = ['explore/', 'replay/', 'points/shop/'].map((path) => root + path)
const steamId = '76561198000000001'
const token = `x.${Buffer.from(JSON.stringify({ sub: steamId, exp: 2000 })).toString('base64url')}.x`
describe('SteamAccountPagePolicyTierTests equivalent native boundaries', () => {
  it('The_store_root_may_be_minted_from', () => {
    for (const url of [root, root.slice(0, -1), root + '?snr=1_4_4__global-header'])
      expect(steamMintAllowed(url)).toBe(true)
  })
  it('Every_fallback_mint_page_is_in_scope', () => {
    for (const page of mintPages) expect(steamMintAllowed(page)).toBe(true)
  })
  it.each([
    'login/',
    'login/?redir=account',
    'join/',
    'twofactor/manage',
    'password/reset',
    'mobilelogin',
    'account/security',
  ])('A_page_the_user_types_credentials_into_is_never_mintable: %s', (path) => {
    expect(steamMintAllowed(root + path)).toBe(false)
    expect(steamNavigationAllowed(root + path)).toBe(true)
  })
  it.each([
    'https://login.steampowered.com/jwt/ajaxrefresh',
    'https://steamcommunity.com/my/edit/info',
    'https://help.steampowered.com/en/',
    'https://store.steampowered.evil.com/',
    'https://store.steampowered.com.evil.com/',
    'http://store.steampowered.com/',
  ])('Nothing_off_the_store_origin_is_mintable: %s', (url) => expect(steamMintAllowed(url)).toBe(false))
  it('A_null_address_is_not_mintable', () => expect(steamMintAllowed(null as unknown as string)).toBe(false))
  it('The_mint_tier_is_a_subset_of_what_the_policy_already_trusts', () => {
    for (const page of [...mintPages, root]) {
      expect(steamMintAllowed(page)).toBe(true)
      expect(steamNavigationAllowed(page)).toBe(true)
      expect(new URL(page).origin).toBe(root.slice(0, -1))
    }
  })
  it('The_harvest_gate_is_left_exactly_as_the_account_page_flow_needs_it', () => {
    expect(steamCapturePage(root)).toBeNull()
    expect(steamCapturePage(root + 'explore/')).toBeNull()
    expect(steamCapturePage(root + 'account/licenses/')).toBe('licenses')
    expect(steamCapturePage(root + 'account/history/')).toBe('history')
  })
  it('The_two_tiers_deliberately_disagree_about_the_store_root', () => {
    expect(steamMintAllowed(root)).toBe(true)
    expect(steamCapturePage(root)).toBeNull()
  })
  it('The_two_account_pages_are_mintable_as_well_as_harvestable', () => {
    for (const path of ['account/licenses/', 'account/history/']) {
      expect(steamMintAllowed(root + path)).toBe(true)
      expect(steamCapturePage(root + path)).not.toBeNull()
    }
  })
})
describe('SteamSignInSessionTests equivalent identity and script behavior', () => {
  it('A_page_and_a_token_naming_the_same_account_agree', () =>
    expect(readSteamIdentity({ token, steamid: steamId }, 1000_000)).toEqual({ token, steamId }))
  it('A_page_and_a_token_naming_different_accounts_do_not_agree', () =>
    expect(() => readSteamIdentity({ token, steamid: '76561198000000002' }, 1000_000)).toThrow(
      'different account',
    ))
  it.each([null, ''])('missing page identity %s does not reject a valid token subject', (steamid) =>
    expect(readSteamIdentity({ token, steamid }, 1000_000)?.steamId).toBe(steamId),
  )
  it.each([[null, steamId], [steamId, null], [null, null], ['', steamId]] as const)(
    'A_missing_identity_on_either_side_is_not_a_disagreement: page=%s subject=%s',
    (page, subject) => expect(steamIdentitiesAgree(page, subject)).toBe(true),
  )
  it('identity agreement does not make a credential without an account valid', () => {
    const missingSubject = `x.${Buffer.from(JSON.stringify({ exp: 2000 })).toString('base64url')}.x`
    expect(readSteamIdentity({ token: missingSubject, steamid: steamId }, 1000_000)).toBeNull()
  })
  it('The_login_page_is_navigable_and_never_mintable', () => {
    expect(steamNavigationAllowed(root + 'login/')).toBe(true)
    expect(steamMintAllowed(root + 'login/')).toBe(false)
  })
  it.each(['config', 'global'])('The_mint_script_reads_the_two_routes_the_spike_documented: %s', (route) => {
    const document = {
      getElementById(id: string) {
        return id === 'application_config'
          ? {
              getAttribute: (attribute: string) =>
                JSON.stringify(
                  attribute === 'data-userinfo'
                    ? { steamid: steamId, logged_in: true }
                    : route === 'config'
                      ? { webapi_token: token }
                      : {},
                ),
            }
          : null
      },
      querySelector: () => null,
    }
    Object.defineProperty(document, 'cookie', {
      get: () => {
        throw Error('Private cookies must not be read')
      },
    })
    const result = runInNewContext(steamTokenProbe, {
      location: new URL(root),
      document,
      window: { g_wapit: route === 'global' ? token : null },
    })
    expect(result).toEqual({ token, steamid: steamId, loggedIn: true })
  })
})

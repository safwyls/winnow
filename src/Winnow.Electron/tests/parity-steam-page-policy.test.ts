import { EventEmitter } from 'node:events'
import { describe, expect, it, vi } from 'vitest'
import type { AccountBrowser } from '../src/main/account-browser'
import { steamNavigationAllowed, steamMintAllowed } from '../src/main/steam-auth-policy'
import { installSteamBrowserPolicy, steamPopupDestination } from '../src/main/steam-browser-policy'
import {
  steamAccountPages,
  steamCaptureLimits,
  steamCapturePage,
  steamPageStep,
} from '../src/main/steam-page-policy'

describe('SteamAccountPagePolicy source decisions', () => {
  it('Only_the_store_origin_can_be_read', () => {
    expect(steamCapturePage('https://store.steampowered.com:443/account/licenses/')).toBe('licenses')
    for (const host of [
      'steamcommunity.com',
      'www.steampowered.com',
      'login.steampowered.com',
      'help.steampowered.com',
    ])
      expect(steamCapturePage(`https://${host}/account/licenses/`)).toBeNull()
    expect(steamCapturePage('https://store.steampowered.com:8443/account/licenses/')).toBeNull()
  })
  it('Valves_login_and_support_origins_are_navigable_but_never_read', () => {
    for (const address of [
      'https://login.steampowered.com/jwt/finalizelogin',
      'https://help.steampowered.com/en/wizard/HelpWithLogin',
      'https://steamcommunity.com/login/home/',
      'https://www.steampowered.com/',
    ]) {
      expect(steamNavigationAllowed(address)).toBe(true)
      expect(steamCapturePage(address)).toBeNull()
      expect(steamMintAllowed(address)).toBe(false)
    }
  })
  it('Anything_outside_the_flow_is_refused_and_a_popup_to_it_leaves_the_window', () => {
    for (const address of [
      'https://evil.example/steal',
      'https://store.steampowered.com.evil.example/account/licenses/',
      'https://steamcommunity.com.evil.example/',
    ]) {
      expect(steamNavigationAllowed(address)).toBe(false)
      expect(steamCapturePage(address)).toBeNull()
      expect(steamPopupDestination(address)).toEqual({ kind: 'external', url: address })
    }
  })
  it('Plaintext_http_on_the_right_host_is_still_refused', () => {
    const address = 'http://store.steampowered.com/account/licenses/'
    expect(steamNavigationAllowed(address)).toBe(false)
    expect(steamCapturePage(address)).toBeNull()
  })
  it('A_non_web_scheme_is_blocked_and_not_handed_anywhere', () => {
    for (const address of [
      'steam://open/games',
      '',
      'file:///private',
      'winnow-app://app/index.html',
      'winnow://fullscreen',
      'javascript:alert(1)',
      'data:text/html,hello',
    ]) {
      expect(steamNavigationAllowed(address)).toBe(false)
      expect(steamPopupDestination(address)).toEqual({ kind: 'block' })
    }
    expect(steamNavigationAllowed('about:blank')).toBe(true)
    expect(steamCapturePage('about:blank')).toBeNull()
    expect(steamMintAllowed('about:blank')).toBe(false)
  })
  it('There_is_no_redirect_to_capture_in_this_flow', () => {
    const address = 'https://store.steampowered.com/login/?redir=account'
    expect(steamNavigationAllowed(address)).toBe(true)
    expect(steamCapturePage(address)).toBeNull()
    expect(steamPopupDestination(address)).toEqual({ kind: 'navigate', url: address })
  })
  it.each([
    ['account/licenses/', 'licenses'],
    ['account/licenses', 'licenses'],
    ['account/LICENSES/', 'licenses'],
    ['account/licenses/?continuationToken=A5F2C1&offset=100', 'licenses'],
    ['account/licenses?offset=900&continuationToken=ZZ', 'licenses'],
    ['account/history/', 'history'],
    ['account/history', 'history'],
    ['account/history/?l=english', 'history'],
  ])('The_two_pages_are_recognised: %s', (path, kind) => {
    expect(steamCapturePage(`https://store.steampowered.com/${path}`)).toBe(kind)
  })
  it.each([
    '/',
    '/account/',
    '/account/registerkey/',
    '/account/licenses/detail/1234',
    '/account/history/detail/1234',
    '/login/?redir=account%2Flicenses',
    '/twofactor/manage',
  ])('Nothing_else_is_ever_read: %s', (path) => {
    expect(steamCapturePage(`https://store.steampowered.com${path}`)).toBeNull()
  })
  it('The_session_only_ever_navigates_to_those_two_pages', () => {
    expect(Object.values(steamAccountPages)).toEqual([
      'https://store.steampowered.com/account/licenses/',
      'https://store.steampowered.com/account/history/',
    ])
    for (const [kind, address] of Object.entries(steamAccountPages))
      expect(steamCapturePage(address)).toBe(kind)
  })
  it('The_cap_is_clamped_into_range_whatever_the_request_asks_for', () => {
    expect(steamCaptureLimits({ maxLoadMoreClicks: 2147483647 }).history).toBe(500)
    expect(steamCaptureLimits({ maxLoadMoreClicks: -5 }).history).toBe(0)
    expect(steamCaptureLimits({ maxLoadMoreClicks: 7 }).history).toBe(7)
    expect(steamCaptureLimits().history).toBe(100)
  })
  it('The_licences_cap_is_clamped_independently_of_the_load_more_one', () => {
    expect(steamCaptureLimits({ maxLicensesPages: 2147483647 }).licenses).toBe(200)
    expect(steamCaptureLimits({ maxLicensesPages: -1 }).licenses).toBe(0)
    expect(steamCaptureLimits({ maxLoadMoreClicks: 3, maxLicensesPages: 12 })).toEqual({
      history: 3,
      licenses: 12,
    })
    expect(steamCaptureLimits().licenses).toBe(50)
  })
  it.each([
    'Clicking_continues_while_the_control_is_there_and_the_rows_grow',
    'The_paginator_is_followed_while_there_is_a_next_page_and_the_rows_grow',
  ])('%s', () => {
    expect(steamPageStep(0, -1, 20, true, 3)).toBe('continue')
    expect(steamPageStep(1, 20, 40, true, 3)).toBe('continue')
  })
  it.each(['Clicking_stops_when_the_control_goes_away', 'The_paginator_stops_when_there_is_no_next_page'])(
    '%s',
    () => {
      expect(steamPageStep(2, 40, 60, false, 3)).toBe('exhausted')
      expect(steamPageStep(0, -1, 42, false, 50)).toBe('exhausted')
    },
  )
  it.each([
    'Clicking_stops_at_the_cap_even_with_more_to_load',
    'The_paginator_stops_at_the_cap_with_more_pages_to_go',
  ])('%s', () => {
    expect(steamPageStep(3, 60, 80, true, 3)).toBe('cap')
    expect(steamPageStep(0, -1, 20, true, 0)).toBe('cap')
  })
  it.each([
    'Clicking_stops_when_a_click_stops_producing_rows',
    'The_paginator_stops_when_a_page_adds_no_rows',
  ])('%s', () => {
    expect(steamPageStep(4, 80, 80, true, 100)).toBe('stalled')
    expect(steamPageStep(4, 80, 12, true, 100)).toBe('stalled')
  })
  it('Both_pages_are_judged_by_one_rule', () => {
    const limits = steamCaptureLimits({ maxLicensesPages: 5, maxLoadMoreClicks: 5 })
    for (const [steps, before, after, present] of [
      [0, -1, 20, true],
      [2, 20, 40, true],
      [5, 40, 60, true],
      [3, 40, 40, true],
      [3, 40, 60, false],
    ] as const)
      expect(steamPageStep(steps, before, after, present, limits.history)).toBe(
        steamPageStep(steps, before, after, present, limits.licenses),
      )
    expect(steamPageStep(5, 40, 40, false, 5)).toBe('exhausted')
    expect(steamPageStep(5, 40, 40, true, 5)).toBe('cap')
  })
})

describe('actual private Steam browser policy wiring', () => {
  function browser() {
    const contents = Object.assign(new EventEmitter(), { setWindowOpenHandler: vi.fn() })
    const value = Object.assign(new EventEmitter(), {
      webContents: contents,
      loadURL: vi.fn().mockResolvedValue(undefined),
      isDestroyed: vi.fn().mockReturnValue(false),
    })
    const external = vi.fn().mockResolvedValue(undefined)
    installSteamBrowserPolicy(value as unknown as AccountBrowser, external)
    const popup = contents.setWindowOpenHandler.mock.calls[0][0]
    return { value, contents, external, popup }
  }
  it('routes Valve popups in place and other web pages outside the private session without creating a child', async () => {
    const { value, external, popup } = browser()
    expect(popup({ url: 'https://help.steampowered.com/en/' })).toEqual({ action: 'deny' })
    expect(value.loadURL).toHaveBeenCalledWith('https://help.steampowered.com/en/')
    expect(popup({ url: 'https://example.com/support' })).toEqual({ action: 'deny' })
    expect(external).toHaveBeenCalledWith('https://example.com/support')
    for (const url of [
      'https://127.0.0.1:4400/private',
      'http://localhost/',
      'winnow-app://app/index.html',
      'steam://run/10',
      'file:///private',
      'https://user:secret@example.com/',
    ])
      expect(popup({ url })).toEqual({ action: 'deny' })
    expect(value.loadURL).toHaveBeenCalledOnce()
    expect(external).toHaveBeenCalledOnce()
    value.isDestroyed.mockReturnValue(true)
    popup({ url: 'https://example.com/closed' })
    expect(external).toHaveBeenCalledOnce()
  })
  it.each(['will-navigate', 'will-redirect'])(
    'applies the same gate to %s and never captures provider redirects',
    (eventName) => {
      const { contents, external } = browser()
      for (const [url, denied] of [
        ['https://store.steampowered.com/login/?redir=account', false],
        ['about:blank', false],
        ['https://example.com/', true],
        ['http://store.steampowered.com/', true],
        ['winnow-app://app/index.html', true],
      ] as const) {
        const event = { preventDefault: vi.fn() }
        contents.emit(eventName, event, url)
        expect(event.preventDefault).toHaveBeenCalledTimes(denied ? 1 : 0)
      }
      expect(external).not.toHaveBeenCalled()
    },
  )
})

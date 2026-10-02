import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import {
  EpicAuthPolicy,
  EpicStrategy,
  epicOrigin,
  readEpicBody,
  validEpicCode,
  validateEpicChallenge,
} from '../src/main/epic-auth-policy'
import type { EpicAuthChallenge, EpicPromptRequest } from '../src/shared/epic'
export const epicRequest = (): EpicPromptRequest => ({
  providerName: 'Epic Games',
  startUrl: 'https://www.epicgames.com/id/authorize?state=fixture-state',
  harvestUrl: 'https://www.epicgames.com/id/api/redirect?clientId=fixture',
  redirectUrl: 'https://localhost/launcher/authorized',
  consentNotice: 'Fixture account consent.',
  redirectCodeParameter: 'code',
  expectedState: 'fixture-state',
  stateParameter: 'state',
  additionalNavigableOrigins: ['https://accounts.google.com'],
  jsonCodeFields: [
    { fieldName: 'authorizationCode', kind: 0 },
    { fieldName: 'exchangeCode', kind: 1 },
  ],
  strategies: 15,
  profileKey: 'epic',
  timeout: '00:10:00',
})
const policy = () => new EpicAuthPolicy(epicRequest())
describe('Epic trusted origins, redirects and body contracts', () => {
  it('never formats captured body credentials into diagnostic strings or JSON', () => {
    const reading = readEpicBody('{"authorizationCode":"SECRET-FIXTURE-CODE"}', epicRequest().jsonCodeFields)
    expect(String(reading)).toContain('redacted')
    expect(String(reading)).not.toContain('SECRET-FIXTURE-CODE')
    expect(JSON.stringify(reading)).not.toContain('SECRET-FIXTURE-CODE')
    expect(reading).toMatchObject({ outcome: 'code', code: 'SECRET-FIXTURE-CODE' })
  })
  it('has no redirect target when the backend registers none', () => {
    const p = new EpicAuthPolicy({ ...epicRequest(), redirectUrl: null })
    expect(p.redirect('https://localhost/launcher/authorized?code=X')).toBe(false)
    expect(p.navigation('https://localhost/launcher/authorized?code=X')).toBe('block')
  })
  it('trusts only request origins and allows configured social origins to render without capture', () => {
    const p = policy()
    expect(p.trusted.sort()).toEqual(['https://localhost:443', 'https://www.epicgames.com:443'])
    expect(p.navigation('https://accounts.google.com/signin')).toBe('allow')
    expect(p.trusts('https://accounts.google.com/signin')).toBe(false)
    expect(p.trusts('https://www.epicgames.com/id/login')).toBe(true)
  })
  it.each([
    'https://www.epicgames.com.evil.example/',
    'https://evil.example/',
    'http://www.epicgames.com/',
    'about:blank',
    'data:text/html,hi',
    '/id/login',
    '',
    null,
  ])('refuses credential messages from untrusted source %s', (source) => {
    expect(epicOrigin(source) && policy().trusts(source!)).toBeFalsy()
  })
  it('binds a redirect to scheme host port and exact path with a harmless trailing slash', () => {
    for (const address of [
      'https://localhost/launcher/authorized?code=X',
      'https://localhost:443/launcher/authorized/?code=X',
    ])
      expect(policy().redirect(address)).toBe(true)
    for (const address of [
      'http://localhost/launcher/authorized',
      'https://localhost:8443/launcher/authorized',
      'https://localhost.evil.test/launcher/authorized',
      'https://localhost/launcher/authorized/extra',
      'https://localhost/other',
      'https://user@localhost/launcher/authorized',
    ])
      expect(policy().redirect(address)).toBe(false)
  })
  it('intercepts registered loopback before navigation and never renders other local addresses', () => {
    expect(policy().navigation('https://localhost/launcher/authorized?code=X')).toBe('redirect')
    for (const address of [
      'https://localhost/',
      'https://127.0.0.1/',
      'winnow-app://app/index.html',
      'file:///C:/secret',
      'steam://run/1',
      'about:config',
    ])
      expect(policy().navigation(address)).toBe('block')
    expect(policy().navigation('about:blank')).toBe('allow')
  })
  it('keeps redirect interception subject to its capture strategy', () => {
    const p = new EpicAuthPolicy({ ...epicRequest(), strategies: EpicStrategy.body })
    expect(p.navigation('https://localhost/launcher/authorized?code=X')).toBe('block')
  })
  it('routes approved popups within the flow, unrelated web links externally, and refuses executable popups', () => {
    expect(policy().popup('https://accounts.google.com/login')).toBe('allow')
    expect(policy().popup('https://localhost/launcher/authorized')).toBe('redirect')
    expect(policy().popup('https://docs.example.test/help')).toBe('external')
    expect(policy().popup('http://docs.example.test/help')).toBe('external')
    expect(policy().popup('steam://run/1')).toBe('block')
  })
  it('requires exact opaque state and distinguishes missing state from mismatch', () => {
    const p = policy(),
      base = 'https://localhost/launcher/authorized?code=X'
    expect(p.state(base + '&state=fixture-state')).toBe('matched')
    expect(p.state(base)).toBe('missing')
    expect(p.state(base + '&state=')).toBe('missing')
    for (const state of ['fixture-stat', 'FIXTURE-STATE', 'other'])
      expect(p.state(base + '&state=' + state)).toBe('mismatch')
    const escaped = new EpicAuthPolicy({ ...epicRequest(), expectedState: 'a+b/c=d' })
    expect(escaped.state(base + '&state=a%2Bb%2Fc%3Dd')).toBe('matched')
    expect(escaped.state(base + '&state=a+b%2Fc%3Dd')).toBe('matched')
    expect(new EpicAuthPolicy({ ...epicRequest(), expectedState: null }).state(base)).toBe('not-required')
  })
  it('permits HTTPS challenge frames without capture or application and loopback navigation', () => {
    expect(policy().frame('https://captcha.example.test/challenge', false)).toBe(true)
    expect(policy().trusts('https://captcha.example.test/challenge')).toBe(false)
    for (const source of [
      'http://captcha.example.test/',
      'winnow-app://app/',
      'file:///C:/secret',
      'https://localhost/launcher/authorized',
    ])
      expect(policy().frame(source, false)).toBe(false)
  })
  it('only treats a same-provider path outside the sign-in journey as a harvest signal', () => {
    expect(policy().leftJourney('https://www.epicgames.com/account/profile')).toBe(true)
    expect(policy().leftJourney('https://www.epicgames.com/id/mfa')).toBe(false)
    expect(policy().leftJourney('https://accounts.google.com/account/profile')).toBe(false)
  })
  it('reads sanitized original fixture bodies as no-session or authorization grant', () => {
    const read = (name: string) =>
      readEpicBody(
        readFileSync(new URL(`../../../tests/fixtures/epic-oauth/${name}.json`, import.meta.url), 'utf8'),
        epicRequest().jsonCodeFields,
      )
    expect(read('redirect-no-session')).toEqual({ outcome: 'no-session' })
    expect(read('redirect-with-code')).toEqual({
      outcome: 'code',
      code: '0123456789abcdef0123456789abcdef',
      kind: 0,
    })
  })
  it('preserves field priority and the exchange grant', () => {
    expect(
      readEpicBody('{"authorizationCode":null,"exchangeCode":"exchange"}', epicRequest().jsonCodeFields),
    ).toEqual({ outcome: 'code', code: 'exchange', kind: 1 })
    expect(
      readEpicBody('{"authorizationCode":"auth","exchangeCode":"exchange"}', epicRequest().jsonCodeFields),
    ).toEqual({ outcome: 'code', code: 'auth', kind: 0 })
  })
  it.each([null, '', '<html>login</html>', '{}', '[]', '"authorizationCode"', '{"error":"changed"}'])(
    'rejects a non-code JSON body %s',
    (value) => {
      expect(readEpicBody(value, epicRequest().jsonCodeFields)).toEqual({ outcome: 'other' })
    },
  )
  it.each([
    '{"authorizationCode":"","exchangeCode":"   "}',
    '{"authorizationCode":false,"exchangeCode":0}',
    '{"authorizationCode":null}',
  ])('recognizes blank or mistyped provider fields as no-session %s', (value) => {
    expect(readEpicBody(value, epicRequest().jsonCodeFields)).toEqual({ outcome: 'no-session' })
  })
  it('never guesses a field and rejects oversized or control-bearing codes', () => {
    expect(readEpicBody('{"authorizationCode":"code"}', [])).toEqual({ outcome: 'other' })
    for (const value of ['', '   ', 'x'.repeat(4097), 'a\nb', 'a\u007fb'])
      expect(validEpicCode(value)).toBe(false)
  })
  it('validates challenge expiration, consent and bounded capture configuration', () => {
    const challenge: EpicAuthChallenge = {
      attemptId: 'a'.repeat(32),
      expiresAt: new Date(Date.now() + 600000).toISOString(),
      request: epicRequest(),
    }
    expect(validateEpicChallenge(challenge)).toBe(challenge)
    for (const value of [
      { ...challenge, expiresAt: 'invalid' },
      { ...challenge, expiresAt: new Date(0).toISOString() },
      { ...challenge, request: { ...epicRequest(), startUrl: 'http://www.epicgames.com/' } },
      { ...challenge, request: { ...epicRequest(), consentNotice: '' } },
      { ...challenge, request: { ...epicRequest(), strategies: 16 } },
    ])
      expect(() => validateEpicChallenge(value)).toThrow('invalid sign-in request')
  })
})

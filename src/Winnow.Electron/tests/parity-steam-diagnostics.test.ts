import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, expect, it } from 'vitest'
import { steamSignInDiagnostic, writeSteamDiagnostic } from '../src/main/steam-diagnostics'

const directories: string[] = []
afterEach(() => {
  for (const directory of directories.splice(0)) rmSync(directory, { recursive: true, force: true })
})
it('writes bounded safe Steam diagnostics and rotates one previous file', () => {
  const directory = mkdtempSync(join(tmpdir(), 'winnow-steam-log-'))
  directories.push(directory)
  const safe = steamSignInDiagnostic({
    signedIn: false,
    outcome: 3,
    detail: 'provider secrets',
    steamId: '76561198000000001',
  })
  writeSteamDiagnostic(directory, safe)
  const file = join(directory, 'logs', 'electron-steam.log')
  expect(readFileSync(file, 'utf8')).toContain('IdentityMismatch')
  expect(readFileSync(file, 'utf8')).not.toMatch(/provider secrets|76561198000000001/)
  writeFileSync(file, 'a'.repeat(512 * 1024))
  writeSteamDiagnostic(directory, safe)
  expect(readFileSync(file + '.previous', 'utf8')).toHaveLength(512 * 1024)
  expect(readFileSync(file, 'utf8').length).toBeLessThan(512)
})
it('rejects arbitrary expiry and outcome text rather than redacting a serialized provider record', () => {
  expect(
    steamSignInDiagnostic({
      signedIn: false,
      outcome: -1,
      expiresAt: 'secret\nsecond line',
      detail: 'token=private',
    }),
  ).toBe('SteamSignInResult(Failed, expires=unknown, access token absent, refresh token absent)')
})

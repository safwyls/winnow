import { join } from 'node:path'
import { mkdtempSync, mkdirSync, writeFileSync, unlinkSync, rmdirSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { describe, expect, it } from 'vitest'
import { frontendDataLocation } from '../src/main/frontend-data-location'

describe('Electron prototype appearance compatibility', () => {
  const appData = 'C:\\test-appdata'
  const current = join(appData, 'Winnow')
  it('keeps current profiles when both generations exist', () => {
    expect(frontendDataLocation(appData, current, () => true)).toBe(current)
  })
  it.each(['Winnow Afterglow', 'winnow-electron'])(
    'reuses %s profiles without moving Chromium or creating an empty replacement',
    (name) => {
      const legacy = join(appData, name)
      expect(frontendDataLocation(appData, current, (path) => path === legacy)).toBe(
        legacy,
      )
    },
  )
  it('uses the new product directory when no previous profiles exist', () => {
    expect(frontendDataLocation(appData, current, () => false)).toBe(current)
  })
  it('does not let an empty current catalogue hide existing prototype preferences', () => {
    const root = mkdtempSync(join(tmpdir(), 'winnow-profile-location-'))
    const destination = join(root, 'Winnow')
    const legacy = join(root, 'Winnow Afterglow')
    const profile = join(legacy, 'libraries', 'existing-library')
    mkdirSync(join(destination, 'libraries'), { recursive: true })
    mkdirSync(profile, { recursive: true })
    writeFileSync(join(profile, 'preferences.json'), '{}')
    try {
      expect(frontendDataLocation(root, destination)).toBe(legacy)
    } finally {
      unlinkSync(join(profile, 'preferences.json'))
      for (const directory of [profile, join(legacy, 'libraries'), legacy, join(destination, 'libraries'), destination, root])
        rmdirSync(directory)
    }
  })
})

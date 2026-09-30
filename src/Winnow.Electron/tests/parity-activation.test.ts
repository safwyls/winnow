import { describe, expect, it } from 'vitest'
import {
  pluginInstallLink,
  registersGlobalProtocol,
  quoteArgument,
  readActivation,
  validateActivationArguments,
  validatedActivation,
} from '../src/main/activation'
describe('native activation arguments', () => {
  it('only the ordinary packaged Windows profile registers a global installation-link handler', () => {
    expect(registersGlobalProtocol(true, 'win32')).toBe(true)
    expect(registersGlobalProtocol(true, 'win32', 'isolated-library')).toBe(false)
    expect(registersGlobalProtocol(false, 'win32')).toBe(false)
    expect(registersGlobalProtocol(true, 'linux')).toBe(false)
    expect(registersGlobalProtocol(true, 'darwin')).toBe(false)
  })
  it('validates structured second-instance messages without interpreting them as arguments', () => {
    expect(validatedActivation({ kind: 'plugin', pluginId: 'xbox', releaseTag: 'v1.2.3' })).toEqual({
      kind: 'plugin',
      pluginId: 'xbox',
      releaseTag: 'v1.2.3',
    })
    expect(validatedActivation({ kind: 'game', ownershipId: 42 })).toEqual({ kind: 'game', ownershipId: 42 })
    expect(validatedActivation({ kind: 'game', ownershipId: '42' })).toEqual({
      kind: 'game',
      ownershipId: 42,
    })
    for (const value of [
      null,
      [],
      { kind: 'unknown' },
      { kind: 'game', ownershipId: 0 },
      { kind: 'game', ownershipId: 1.5 },
      { kind: 'game', ownershipId: Number.MAX_SAFE_INTEGER + 1 },
      { kind: 'plugin', pluginId: 'x'.repeat(65535), releaseTag: 'v1.2.3' },
      { kind: 'plugin', pluginId: 'xbox', releaseTag: 'v1.2.3' + 'x'.repeat(65535) },
      '--jump-list-fullscreen',
      { kind: 'game', ownershipId: -1 },
      { kind: 'game', ownershipId: '042' },
      { kind: 'plugin', pluginId: 'xbox&url=https://evil.test', releaseTag: 'v1.2.3' },
      { kind: 'plugin', pluginId: 'xbox', releaseTag: 'v1.2.3 --data-dir C:\\other' },
    ])
      expect(validatedActivation(value)).toBeNull()
  })
  it('validates URI launch arguments before accepting a data directory', () => {
    const uri = 'winnow://plugins/install?id=xbox&release=v0.2.0'
    for (const args of [['--uri', uri], [uri], ['--data-dir', 'C:\\Temp\\isolated library', '--uri', uri]])
      expect(() => validateActivationArguments(args)).not.toThrow()
    for (const args of [
      ['--uri'],
      ['--uri', uri, '--data-dir', 'C:\\untrusted'],
      ['--no-sync', '--uri', uri],
      ['--uri', uri + '" --data-dir C:\\untrusted'],
      ['--uri', 'https://example.com/plugin.zip'],
      [uri, uri],
      ['--jump-list-fullscreen', '--uri', uri],
    ])
      expect(() => validateActivationArguments(args)).toThrow('startup arguments are invalid')
  })
  it('accepts only an official provider and exact release selection', () => {
    expect(pluginInstallLink('winnow://plugins/install?id=xbox&release=v1.2.3-beta.4')).toEqual({
      kind: 'plugin',
      pluginId: 'xbox',
      releaseTag: 'v1.2.3-beta.4',
    })
    expect(
      readActivation([
        '--data-dir',
        'C:\\isolated library',
        '--uri',
        'winnow://plugins/install?release=v1.2.3&id=psn',
      ]),
    ).toEqual({ kind: 'plugin', pluginId: 'psn', releaseTag: 'v1.2.3' })
  })
  it.each([
    'winnow://plugins/install?id=evil&release=v1.2.3',
    'winnow://plugins/install?id=xbox&id=psn',
    'winnow://plugins/install?id=xbox&release=v01.2.3',
    'winnow://plugins/install?id=xbox&release=v1.2.3%20--data-dir',
    'winnow://plugins/install?id=xbox&release=v1.2.3&url=https://evil.test/',
    'winnow://plugins/install?id=xbox&release=v1.2.3#fragment',
  ])('rejects noncanonical links: %s', (value) => expect(pluginInstallLink(value)).toBeNull())
  it('does not treat a browser URI as additional process arguments', () => {
    expect(
      readActivation(['winnow://plugins/install?id=xbox&release=v1.2.3', '--data-dir', 'C:\\other']),
    ).toEqual({ kind: 'show' })
    expect(readActivation(['--uri=winnow://plugins/install?id=xbox&release=v1.2.3'])).toEqual({
      kind: 'show',
    })
  })
  it('limits Jump List actions to a single valid local ownership ID or fullscreen', () => {
    expect(readActivation(['--jump-list-game', '42', '--data-dir', 'C:\\games'])).toEqual({
      kind: 'game',
      ownershipId: 42,
    })
    expect(readActivation(['--jump-list-fullscreen'])).toEqual({ kind: 'fullscreen' })
    for (const args of [
      ['--jump-list-game', '0'],
      ['--jump-list-game'],
      ['--jump-list-game', 'steam://run/42'],
      ['--jump-list-game', '9223372036854775808'],
      ['--jump-list-unknown'],
      ['--jump-list-game', '-1'],
      ['--jump-list-game', '42', '--jump-list-fullscreen'],
    ])
      expect(readActivation(args)).toEqual({ kind: 'show' })
  })
  it('quotes isolated paths without changing trailing slashes or quotes', () => {
    expect(quoteArgument('C:\\My games\\')).toBe('"C:\\My games\\\\"')
    expect(quoteArgument('a"b')).toBe('"a\\"b"')
  })
})

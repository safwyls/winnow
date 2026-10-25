import { describe, expect, it } from 'vitest'
import { loginItemOptions } from '../src/main/login-options'

describe('Windows registration options', () => {
  it('passes the source executable and background argument separately to Electron for native quoting', () => {
    expect(loginItemOptions('C:\\Program Files\\Winnow\\Winnow.exe')).toEqual({
      path: 'C:\\Program Files\\Winnow\\Winnow.exe',
      args: ['--background'],
    })
  })
  it('uses identical executable and arguments for reading and writing an isolated registration', () => {
    const executable = 'C:\\Program Files\\Winnow\\Winnow.exe'
    const root = 'C:\\Library fixtures\\Selected library'
    const read = loginItemOptions(executable, root)
    const write = { ...loginItemOptions(executable, root), openAtLogin: true }
    expect(read).toEqual({ path: executable, args: ['--background', '--data-dir', root] })
    expect(write).toEqual({ ...read, openAtLogin: true })
    expect({ ...loginItemOptions(executable, root), openAtLogin: false }).toEqual({
      ...read,
      openAtLogin: false,
    })
  })
  it('does not turn a resolved default or legacy data root into an explicit isolated launch', () => {
    const options = loginItemOptions('C:\\Program Files\\Winnow\\Winnow.exe', undefined)
    expect(options.args).toEqual(['--background'])
    expect(options.args).not.toContain('--data-dir')
  })
})

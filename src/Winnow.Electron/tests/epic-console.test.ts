import { EventEmitter } from 'node:events'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { epicConsoleArguments, epicConsoleRequested, runEpicConsole } from '../src/main/epic-console'

describe('terminal Epic entrypoint', () => {
  it('only recognizes the explicit command and preserves both code/root forms without forwarding GUI flags or spoofed parents', () => {
    expect(epicConsoleRequested(['--code=example'])).toBe(false)
    expect(epicConsoleRequested(['--epic-login'])).toBe(true)
    expect(
      epicConsoleArguments(
        [
          '--epic-login',
          '--code',
          'fixture-code',
          '--data-dir',
          'root with spaces',
          '--fullscreen',
          '--console-owner-pid',
          '99',
        ],
        12,
        13,
      ),
    ).toEqual([
      '--epic-login',
      '--code',
      'fixture-code',
      '--data-dir',
      'root with spaces',
      '--console-owner-pid',
      '12',
      '--terminal-parent-pid',
      '13',
    ])
    expect(epicConsoleArguments(['--code=fixture', '--data-dir=/tmp/isolated', '--no-sync'], 12, 13)).toEqual(
      [
        '--epic-login',
        '--code=fixture',
        '--data-dir=/tmp/isolated',
        '--no-sync',
        '--console-owner-pid',
        '12',
        '--terminal-parent-pid',
        '13',
      ],
    )
  })

  it.each([0, 1, 2, 3])(
    'delegates terminal streams to the actual backend companion and returns exit %i',
    async (code) => {
      const child = new EventEmitter()
      const launch = vi.fn(() => child)
      const backend = resolve('isolated/backend/Winnow.Backend.exe')
      const pending = runEpicConsole(
        ['--epic-login', '--code=fixture'],
        {
          appPath: resolve('.'),
          resourcesPath: resolve('resources'),
          environment: {
            WINNOW_BACKEND_PATH: backend,
            WINNOW_ACTIVATION_HELPER_PATH: resolve('not-console.exe'),
          },
          exists: async () => {},
        },
        launch as never,
      )
      await vi.waitFor(() => expect(launch).toHaveBeenCalledOnce())
      expect(launch.mock.calls[0]).toMatchObject([
        backend,
        expect.arrayContaining(['--epic-login', '--code=fixture']),
        { stdio: 'inherit', windowsHide: true },
      ])
      child.emit('exit', code)
      expect(await pending).toBe(code)
    },
  )
})

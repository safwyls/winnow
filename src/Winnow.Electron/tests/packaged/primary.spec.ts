import { test, expect } from '@playwright/test'
import { execFile } from 'node:child_process'
import { mkdir, mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { promisify } from 'node:util'

test.skip(
  !['win32', 'linux'].includes(process.platform),
  'Primary packages target Windows and supported Ubuntu.',
)
for (const mode of ['desktop', 'fullscreen'] as const)
  test(`primary packaged ${mode} uses its own runtime, renderer, backend and update guard`, async ({}, info) => {
    const executable = resolve(
      process.env.WINNOW_PACKAGED_EXE ??
        (process.platform === 'win32' ? 'release/win-unpacked/Winnow.exe' : 'release/linux-unpacked/Winnow'),
    )
    const temporary = resolve('../..', '.tmp')
    await mkdir(temporary, { recursive: true })
    const directory = await mkdtemp(join(temporary, 'winnow primary packaged '))
    const report = info.outputPath(`${mode}-package-health.json`)
    await promisify(execFile)(
      process.execPath,
      [
        resolve('tests/packaged/probe.mjs'),
        '--exe',
        executable,
        '--data-dir',
        directory,
        '--report',
        report,
        '--mode',
        mode,
      ],
      { windowsHide: true, timeout: 55000 },
    )
    const result = JSON.parse(await readFile(report, 'utf8'))
    expect(result).toMatchObject({
      passed: true,
      closed: true,
      errors: [],
      pageErrors: [],
      mode,
      libraryReadSucceeded: true,
      presentationReadyBeforeCapture: true,
      activation: { secondaryExitCode: 0, sameBackend: true },
      nativeWindow: { visible: true, fullscreen: mode === 'fullscreen', nativeHandlePresent: true },
    })
    if (process.platform === 'win32')
      expect(result).toMatchObject({ portableLeaseDeniedWrite: true, portableLeaseReleased: true })
    else {
      expect(result).toMatchObject({
        linuxProcessesTerminated: true,
        rendererSandbox: {
          preferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
          process: { noNewPrivileges: 1, seccomp: 2, disabledSandboxArguments: [] },
          nodeIntegrationExposed: false,
        },
      })
      if (result.package.managed) {
        expect(result.managedUpdate).toMatchObject({
          marker: true,
          portableHelperAbsent: true,
          adjacentLeaseAbsent: true,
          downloaded: { canDownload: false, canRestart: false },
          restarted: { canDownload: false, canRestart: false },
        })
        expect(result.managedLauncher).toMatchObject({
          path: '/usr/bin/winnow',
          activation: { kind: mode === 'fullscreen' ? 'fullscreen' : 'show' },
          plugin: {
            uri: 'winnow://plugins/install?id=psn&release=v0.2.0',
            expected: { kind: 'plugin', pluginId: 'psn', releaseTag: 'v0.2.0' },
            secondaryExitCode: 0,
            sameBackend: true,
            rendererDeliveryIntercepted: true,
          },
        })
      } else
        expect(result).toMatchObject({
          linuxPortableGuard: { exclusiveFlockDenied: true },
          portableLeaseReleased: true,
        })
    }
  })

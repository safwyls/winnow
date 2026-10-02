import { test, expect } from '@playwright/test'
import { execFile } from 'node:child_process'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { promisify } from 'node:util'

test.skip(
  process.platform !== 'win32',
  'Primary Windows package verification; Linux package checks run separately.',
)
for (const mode of ['desktop', 'fullscreen'] as const)
  test(`primary packaged ${mode} uses its own runtime, renderer, backend and update guard`, async ({}, info) => {
    const executable = resolve(process.env.WINNOW_PACKAGED_EXE ?? 'release/win-unpacked/Winnow.exe')
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow primary packaged '))
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
      portableLeaseDeniedWrite: true,
      portableLeaseReleased: true,
      activation: { secondaryExitCode: 0, sameBackend: true },
    })
  })

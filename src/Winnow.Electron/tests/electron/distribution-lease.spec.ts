import { test, expect, _electron as electron } from '@playwright/test'
import { build } from 'esbuild'
import { access, mkdir, mkdtemp, open, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

test('actual Electron portable guard acknowledges readiness and releases its native installation lease', async ({}, info) => {
  test.skip(
    process.platform !== 'win32',
    'This native contract measures Windows kernel share-mode exclusion; POSIX file leases use separate tests.',
  )
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-distribution-lease-'))
  const installation = join(directory, 'portable'),
    dataDirectory = join(directory, 'selected library')
  const helper = resolve(
    process.env.WINNOW_UPDATE_HELPER_PATH ??
      '../Winnow.Update.Helper/bin/Debug/net10.0/Winnow.Update.Helper.exe',
  )
  await access(helper)
  await mkdir(installation)
  await mkdir(dataDirectory)
  const report = info.outputPath('distribution-lease-ledger.json'),
    module = join(directory, 'distribution-helper.mjs')
  await build({
    entryPoints: ['src/main/distribution-helper.ts'],
    outfile: module,
    bundle: true,
    platform: 'node',
    format: 'esm',
    packages: 'external',
  })
  const application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/distribution-lease-main.mjs'), '--data-dir', dataDirectory],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_UPDATE_HELPER_PATH: helper,
      WINNOW_LEASE_REPORT: report,
      WINNOW_LEASE_MODULE: module,
      WINNOW_LEASE_INSTALLATION: installation,
      WINNOW_LEASE_DATA: dataDirectory,
    },
    chromiumSandbox: true,
  })
  const lock = join(directory, '.portable.winnow-update.lock')
  const lockWritable = async () => {
    const file = await open(lock, 'r+')
    await file.close()
  }
  try {
    const page = await application.firstWindow()
    await expect(page.getByRole('heading', { name: 'Portable update guard' })).toBeVisible()
    const state = () => application.evaluate(() => (globalThis as any).__lease.state)
    expect((await state()).events).toEqual([{ kind: 'leased', canUpdate: true, recoveryStatus: undefined }])
    expect((await state()).children[0].args).toEqual([
      'frontend',
      '--installation',
      installation,
      '--data-dir',
      dataDirectory,
      '--pid',
      String((await state()).processId),
    ])
    expect((await state()).children[0].stdout).not.toContain('"kind":"ready"')
    await expect(lockWritable()).rejects.toMatchObject({ code: expect.stringMatching(/EACCES|EPERM|EBUSY/) })
    await application.evaluate(() => (globalThis as any).__lease.ready())
    await expect.poll(async () => (await state()).children[0].stdout).toContain('"kind":"ready"')
    await application.evaluate(() => (globalThis as any).__lease.dispose())
    await expect
      .poll(async () => {
        try {
          await lockWritable()
          return true
        } catch {
          return false
        }
      })
      .toBe(true)
    await expect
      .poll(
        async () => (await state()).children[0].exit !== null || (await state()).children[0].signal !== null,
      )
      .toBe(true)
    await application.evaluate(() => (globalThis as any).__lease.start())
    expect((await state()).children).toHaveLength(2)
    await application.evaluate(() => (globalThis as any).__lease.ready())
    await expect(lockWritable()).rejects.toMatchObject({ code: expect.stringMatching(/EACCES|EPERM|EBUSY/) })
    expect((await state()).errors).toEqual([])
  } finally {
    await closeFixture(application)
  }
  await expect
    .poll(async () => {
      try {
        await lockWritable()
        return true
      } catch {
        return false
      }
    })
    .toBe(true)
  const ledger = JSON.parse(await readFile(report, 'utf8'))
  expect(ledger.errors).toEqual([])
  for (const child of ledger.children) {
    await expect
      .poll(() => {
        try {
          process.kill(child.pid, 0)
          return true
        } catch (error) {
          if ((error as NodeJS.ErrnoException).code === 'ESRCH') return false
          throw error
        }
      })
      .toBe(false)
  }
  await writeFile(
    report,
    JSON.stringify(
      {
        ...ledger,
        helper,
        installation,
        dataDirectory,
        finalLockWritable: true,
        allChildrenExited: true,
        scope:
          'Actual Electron main module and .NET helper protocol/Windows file lease; no backend or update installation.',
      },
      null,
      2,
    ),
  )
})

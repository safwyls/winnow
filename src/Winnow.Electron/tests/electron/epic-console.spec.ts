import { expect, test } from '@playwright/test'
import electronPath from 'electron'
import { spawn, type ChildProcess } from 'node:child_process'
import { mkdtemp, readFile, stat } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { prebuiltBackend, prebuiltFixture } from './prebuilt-backend'
import { closeFixture } from './fixture-cleanup'

function cleanEnvironment() {
  const env = { ...process.env }
  for (const key of Object.keys(env))
    if (/^(IGDB|TWITCH|STEAM|EPIC|GOG)(?:__|_|$)/i.test(key) || key === 'ELECTRON_RUN_AS_NODE')
      delete env[key]
  return env
}

async function exit(child: ChildProcess, timeout = 20000): Promise<number | null> {
  if (child.exitCode !== null || child.signalCode !== null) return child.exitCode
  return new Promise((done, fail) => {
    const timer = setTimeout(
      () => fail(new Error(`Owned terminal process ${child.pid} did not exit`)),
      timeout,
    )
    child.once('error', (error) => {
      clearTimeout(timer)
      fail(error)
    })
    child.once('exit', (code) => {
      clearTimeout(timer)
      done(code)
    })
  })
}

for (const form of ['separate', 'equals', 'stdin', 'eof'] as const) {
  test(`terminal Epic command ${form} preserves redirected handles, real API and no GUI`, async ({}, info) => {
    const directory = await mkdtemp(join(tmpdir(), 'winnow-electron-platform-context-terminal-'))
    const backend = spawn(prebuiltFixture, ['--data-dir', directory], {
      env: cleanEnvironment(),
      windowsHide: true,
      stdio: 'ignore',
    })
    let child: ChildProcess | undefined
    let stdout = '',
      stderr = ''
    try {
      let endpoint!: { address: string; token: string; processId: number }
      await expect
        .poll(async () => {
          try {
            endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
            return true
          } catch {
            return false
          }
        })
        .toBe(true)
      const seed = await fetch(new URL('/__fixture/platform-context/seed', endpoint.address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ kind: 'platforms' }),
      })
      expect(seed.ok).toBe(true)
      const report = info.outputPath('terminal-lifecycle.json')
      const args = [resolve('tests/electron/epic-console-main.mjs'), '--epic-login', '--data-dir', directory]
      if (form === 'separate') args.push('--code', 'fixture-code')
      if (form === 'equals') args.push('--code=fixture-code')
      child = spawn(String(electronPath), args, {
        env: { ...cleanEnvironment(), WINNOW_BACKEND_PATH: prebuiltBackend, WINNOW_TERMINAL_REPORT: report },
        windowsHide: true,
        stdio: ['pipe', 'pipe', 'pipe'],
      })
      child.stdout!.on('data', (chunk) => {
        stdout += chunk.toString()
      })
      child.stderr!.on('data', (chunk) => {
        stderr += chunk.toString()
      })
      child.stdin!.end(form === 'stdin' ? 'fixture-code\n' : '')
      expect(await exit(child)).toBe(form === 'eof' ? 1 : 0)
      expect(JSON.parse(await readFile(report, 'utf8'))).toMatchObject({
        windows: 0,
        exitCode: form === 'eof' ? 1 : 0,
      })
      expect(stdout + stderr).not.toContain('fixture-code')
      expect(
        await stat(join(directory, 'electron-userdata')).then(
          () => true,
          () => false,
        ),
      ).toBe(false)
      const response = await fetch(new URL('/api/v1/connections/stores', endpoint.address), {
        headers: { Authorization: `Bearer ${endpoint.token}` },
      })
      expect(response.ok).toBe(true)
      const connections = await response.json()
      expect(connections.epic.displayName).toBe(form === 'eof' ? 'Account A' : 'Account B')
      if (form === 'eof') {
        expect(stdout).toContain('Press Enter')
        expect(stdout).not.toContain('Paste the code here:')
        expect(stderr).toContain('Sign-in cancelled.')
      } else expect(stdout).toContain('Signed in. The backend is refreshing your Epic library.')
      await info.attach('terminal-output', {
        body: JSON.stringify({ stdout, stderr, backendRemainedAlive: true }),
        contentType: 'application/json',
      })
    } finally {
      if (child && child.exitCode === null && child.signalCode === null) {
        child.kill()
        await exit(child)
      }
      await closeFixture(undefined, directory)
      expect(await exit(backend)).toBe(0)
    }
  })
}

import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { build } from 'vite'
import react from '@vitejs/plugin-react'
import { mkdtemp, writeFile } from 'node:fs/promises'
import { join, relative, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page
test.beforeAll(async () => {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-dragon-')),
    source = relative(directory, resolve('tests/electron/dragon-probe-renderer.tsx')).replaceAll('\\', '/')
  await writeFile(
    join(directory, 'index.html'),
    `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; worker-src 'self'"></head><body><div id="root"></div><script type="module" src="${source}"></script></body></html>`,
  )
  await build({
    configFile: false,
    root: directory,
    base: './',
    plugins: [react()],
    logLevel: 'silent',
    build: { outDir: join(directory, 'out'), emptyOutDir: false },
  })
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/actions-probe-main.mjs'),
      '--data-dir',
      directory,
      join(directory, 'out/index.html'),
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  await expect(page.getByRole('button', { name: 'Start trace', exact: true })).toBeVisible()
})
test.afterAll(async () => closeFixture(application))

test('all fifteen native contours retain a thirteen percent trail and exact head across 101 phases', async () => {
  const contours = await page.evaluate(() => (window as any).dragonProbe.contours())
  expect(contours).toHaveLength(15)
  for (const contour of contours) {
    expect(contour.path).toMatch(/^M.*[zZ]\s*$/)
    expect(contour.length).toBeGreaterThan(0)
    expect(contour.phases).toHaveLength(101)
    for (const sample of contour.phases) {
      expect(sample.trail).toBeCloseTo(contour.length * 0.13, 6)
      expect(sample.headError).toBeLessThanOrEqual(0.1)
    }
  }
})
for (const light of [false, true])
  for (const size of [88, 100])
    test(`native drawing retains ink and glow across nine positions with light ${light} at ${size}px`, async ({}, info) => {
      const ink = light ? '#2f4f4f' : '#faebd7',
        glow = light ? '#008080' : '#40e0d0',
        hashes = []
      await page.evaluate((light) => {
        document.body.style.background = light ? '#f5f5f5' : '#0f1c1e'
      }, light)
      for (let position = 0; position <= 8; position++) {
        const pixels = await page.evaluate(
          ({ ink, glow, size, phase }) => (window as any).dragonProbe.sample(ink, glow, size, phase),
          { ink, glow, size, phase: position / 8 },
        )
        expect(pixels.inkPixels).toBeGreaterThan(size)
        expect(pixels.glowPixels).toBeGreaterThan(size / 2)
        expect(pixels.visible).toBeGreaterThan(size * 2)
        hashes.push(pixels.hash)
        await page.locator('#sample').screenshot({ path: info.outputPath(`trace-${position}.png`) })
      }
      expect(new Set(hashes.slice(0, 8)).size).toBe(8)
      expect(hashes[8]).toBe(hashes[0])
    })
test('worker theme and size changes retain progress and disabling or detaching releases the clock', async ({}, info) => {
  await page.evaluate(() => {
    document.body.style.background = '#0f1c1e'
  })
  const mark = page.locator('.startup-dragon')
  await page.getByRole('button', { name: 'Start trace', exact: true }).click()
  await expect(mark).toHaveAttribute('data-worker', 'true')
  await expect.poll(() => mark.getAttribute('data-elapsed').then(Number)).toBeGreaterThanOrEqual(1800)
  const elapsed = Number(await mark.getAttribute('data-elapsed')),
    frames = Number(await mark.getAttribute('data-frames'))
  await page.evaluate(() => (window as any).dragonProbe.appearance('#ffffff', '#008080', 140))
  await expect(mark).toHaveAttribute('data-ink', 'rgb(255, 255, 255)')
  await expect(mark).toHaveAttribute('data-glow', '#008080')
  await expect(mark).toHaveAttribute(
    'data-size',
    String(await page.evaluate(() => Math.ceil(140 * devicePixelRatio))),
  )
  expect(Number(await mark.getAttribute('data-elapsed'))).toBeGreaterThanOrEqual(elapsed)
  expect(Number(await mark.getAttribute('data-frames'))).toBeGreaterThan(frames)
  await mark.screenshot({ path: info.outputPath('worker-updated.png') })
  await page.getByRole('button', { name: 'Stop trace', exact: true }).click()
  await expect(mark).not.toHaveAttribute('data-worker')
  expect(await mark.locator('canvas').count()).toBe(0)
  await page.getByRole('button', { name: 'Start trace', exact: true }).click()
  await expect(mark).toHaveAttribute('data-worker', 'true')
  expect(Number(await mark.getAttribute('data-elapsed'))).toBeLessThan(1800)
  await page.getByRole('button', { name: 'Detach dragon', exact: true }).click()
  await expect(mark).toHaveCount(0)
  const before = await page.evaluate(() => (window as any).dragonProbe.frames.length)
  await page.evaluate(async () => {
    const start = performance.now()
    while (performance.now() - start < 300)
      await new Promise<void>((done) => requestAnimationFrame(() => done()))
  })
  expect(await page.evaluate(() => (window as any).dragonProbe.frames.length)).toBe(before)
  expect(await page.evaluate(() => (window as any).dragonProbe.failures)).toBe(0)
  await page.getByRole('button', { name: 'Attach dragon', exact: true }).click()
  await expect(mark).toHaveAttribute('data-worker', 'true')
  expect(Number(await mark.getAttribute('data-elapsed'))).toBeLessThan(1800)
  await page.getByRole('button', { name: 'Stop trace', exact: true }).click()
})

import { _electron as electron, expect, test, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { build } from 'esbuild'
import { mkdir, mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'

const output = resolve('../..', '.tmp/typography-popout')
let app: ElectronApplication | undefined
test.beforeAll(async () => {
  await mkdir(output, { recursive: true })
  await build({
    entryPoints: [resolve('tests/electron/typography-popout-main.ts')],
    outfile: join(output, 'main.cjs'),
    bundle: true,
    platform: 'node',
    format: 'cjs',
    external: ['electron'],
    plugins: [
      {
        name: 'real-inline-fonts',
        setup(bundle) {
          bundle.onResolve({ filter: /\.ttf\?inline$/ }, (args) => ({
            path: resolve(args.resolveDir, args.path.replace(/\?inline$/, '')),
            namespace: 'font',
          }))
          bundle.onLoad({ filter: /.*/, namespace: 'font' }, async (args) => ({
            contents: await readFile(args.path),
            loader: 'dataurl',
          }))
        },
      },
    ],
    logLevel: 'silent',
  })
})
test.afterEach(async () => closeFixture(app))

test('trusted fullscreen native account and reference popouts keep live source typography draft focus and provider separation', async () => {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-typography-popout-'))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [join(output, 'main.cjs'), '--data-dir', directory, '--force-color-profile=srgb'],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  const measurements: unknown[] = []
  const local = async () => {
    await expect.poll(() => app!.windows().some((page) => page.url().startsWith('about:blank'))).toBe(true)
    return app!.windows().find((page) => page.url().startsWith('about:blank'))!
  }
  const size = async (controls: Page, percent: number) => {
    await app!.evaluate((_electron, value) => (globalThis as any).typographyPopout.size(value), percent)
    await expect(controls.locator('html')).toHaveCSS('--theme-text-scale', String(percent / 100))
    await controls.evaluate(() => document.fonts.ready)
  }
  const press = async (button: number) => {
    for (const pressed of [[], [button], []]) {
      await app!.evaluate((_electron, value) => {
        ;(globalThis as any).typographyPopout.state.pressed = value
      }, pressed)
      await new Promise((done) => setTimeout(done, 120))
    }
  }
  const providerBoundary = async (controls: Page, label: string) => {
    await controls.evaluate(() => document.fonts.ready)
    const bottom = await controls.locator('body').evaluate((node) => node.getBoundingClientRect().bottom)
    await expect
      .poll(
        async () => (await app!.evaluate(() => (globalThis as any).typographyPopout.geometry())).provider.y,
      )
      .toBeGreaterThanOrEqual(Math.ceil(bottom))
    const geometry = await app!.evaluate(() => (globalThis as any).typographyPopout.geometry())
    expect(geometry.provider.y + geometry.provider.height).toBe(geometry.window.height)
    expect(geometry.provider.height).toBeGreaterThan(200)
    measurements.push({ label, bottom, geometry })
  }
  try {
    const controls = await local()
    await expect
      .poll(() =>
        app!
          .context()
          .pages()
          .some((page) => page.url() === 'https://account.example.test/login'),
      )
      .toBe(true)
    const provider = app!
      .context()
      .pages()
      .find((page) => page.url() === 'https://account.example.test/login')!
    await size(controls, 120)
    await expect(controls.locator('.account-input-hints')).toHaveCSS('font-size', '28.8px')
    await providerBoundary(controls, 'account 1920 at 120')
    await app!.evaluate(() => {
      const window = (globalThis as any).typographyPopout.current()
      window.setContentSize(760, 720)
      window.focus()
    })
    await providerBoundary(controls, 'account wrapped 760 at 120')
    await controls.screenshot({ path: test.info().outputPath('account-wrapped-hints-120.png') })
    await app!.evaluate(() => {
      const window = (globalThis as any).typographyPopout.current()
      window.setContentSize(1920, 1080)
      window.focus()
    })
    await provider.getByLabel('Password').fill('provider-kept-')
    await press(3)
    const draft = controls.getByLabel('Text to insert')
    await expect(draft).toHaveAttribute('type', 'password')
    await expect(draft).toHaveValue('')
    await draft.fill('draft-kept')
    await expect(draft).toBeFocused()
    for (const percent of [120, 80, 100, 120]) {
      await size(controls, percent)
      await expect(draft).toHaveValue('draft-kept')
      await expect(draft).toBeFocused()
      const values = await controls.evaluate(() => {
        const metric = (selector: string) => {
          const node = document.querySelector(selector)!
          const s = getComputedStyle(node)
          return { size: parseFloat(s.fontSize), line: parseFloat(s.lineHeight), family: s.fontFamily }
        }
        return {
          copy: metric('main > p'),
          key: metric('nav a'),
          input: metric('#draft'),
          hints: metric('main > small'),
        }
      })
      for (const key of ['copy', 'key', 'input'] as const) {
        expect(values[key].size).toBeCloseTo((28 * percent) / 100, 2)
        expect(values[key].line).toBeCloseTo((42 * percent) / 100, 2)
        expect(values[key].family).toContain('Avalon Data')
      }
      expect(values.hints.size).toBeCloseTo((24 * percent) / 100, 2)
      measurements.push({ percent, values })
    }
    const cdp = await controls.context().newCDPSession(controls)
    await cdp.send('DOM.enable')
    await cdp.send('CSS.enable')
    const { root } = await cdp.send('DOM.getDocument')
    const { nodeId } = await cdp.send('DOM.querySelector', { nodeId: root.nodeId, selector: 'main > p' })
    const { fonts } = await cdp.send('CSS.getPlatformFontsForNode', { nodeId })
    expect(fonts.some((font) => font.familyName === 'IBM Plex Mono' && font.isCustomFont)).toBe(true)
    measurements.push({ renderedPopoutFonts: fonts })
    await cdp.detach()
    await controls.screenshot({ path: test.info().outputPath('native-account-keyboard-120.png') })
    await press(1)
    await expect(draft).toHaveCount(0)
    await expect(provider.getByLabel('Password')).toHaveValue('provider-kept-')
    await expect(provider.locator('body')).toHaveCSS('font-size', '24px')
    expect(await app!.evaluate(() => (globalThis as any).typographyPopout.state.providerScriptCalls)).toBe(0)
    await press(1)
    await expect.poll(() => app!.evaluate(() => !!(globalThis as any).typographyPopout.current())).toBe(false)
    await app!.evaluate(() => (globalThis as any).typographyPopout.reference())
    const reference = await local()
    await app!.evaluate(() => {
      const window = (globalThis as any).typographyPopout.current()
      window.setFullScreen(false)
      window.setContentSize(1920, 1080)
      window.focus()
    })
    await size(reference, 120)
    await providerBoundary(reference, 'reference 1920 at 120')
    await app!.evaluate(() => {
      const window = (globalThis as any).typographyPopout.current()
      window.setContentSize(760, 720)
      window.focus()
    })
    await providerBoundary(reference, 'reference wrapped 760 at 120')
    const wrapped = await reference.evaluate(() => ({
      width: innerWidth,
      scrollWidth: document.documentElement.scrollWidth,
      bodyScrollWidth: document.body.scrollWidth,
      actions: [...document.querySelectorAll('nav a')].map((node) => ({
        label: node.textContent,
        ...node.getBoundingClientRect().toJSON(),
      })),
    }))
    expect(wrapped.scrollWidth).toBeLessThanOrEqual(wrapped.width)
    expect(wrapped.bodyScrollWidth).toBeLessThanOrEqual(wrapped.width)
    expect(wrapped.actions.map((action) => action.label)).toEqual([
      'Back',
      'Forward',
      'Open in browser',
      'Close',
    ])
    for (const action of wrapped.actions) {
      expect(action.x).toBeGreaterThanOrEqual(0)
      expect(action.right).toBeLessThanOrEqual(wrapped.width)
      expect(action.height).toBeGreaterThanOrEqual(44)
    }
    measurements.push({ wrapped })
    await reference.screenshot({ path: test.info().outputPath('reference-wrapped-hints-120.png') })
    expect(await app!.evaluate(() => (globalThis as any).typographyPopout.state.forbidden)).toEqual([])
  } finally {
    await test.info().attach('native-popout-typography', {
      body: JSON.stringify(measurements),
      contentType: 'application/json',
    })
    for (const page of app!.windows())
      if (!page.isClosed() && page.url().startsWith('about:blank'))
        await page.screenshot({ path: test.info().outputPath('popout-before-teardown.png') })
  }
})

import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Locator,
  type Page,
} from '@playwright/test'
import electronPath from 'electron'
import { build } from 'esbuild'
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'

const probe = resolve('../..', '.tmp/theme-contrast-native-probe')
let app: ElectronApplication | undefined
let page: Page
const rows: unknown[] = []
const errors: string[] = []

test.beforeAll(async () => {
  await mkdir(probe, { recursive: true })
  await build({
    entryPoints: [resolve('tests/electron/theme-contrast-probe.tsx')],
    outfile: join(probe, 'probe.js'),
    bundle: true,
    platform: 'browser',
    format: 'iife',
    jsx: 'automatic',
    define: { 'process.env.NODE_ENV': '"production"' },
    loader: { '.ttf': 'file', '.woff': 'file', '.woff2': 'file', '.svg': 'dataurl' },
    plugins: [
      {
        name: 'source-theme-text',
        setup(bundle) {
          bundle.onResolve({ filter: /\?raw$/ }, (args) => ({
            path: resolve(args.resolveDir, args.path.slice(0, -4)),
            namespace: 'source-theme-text',
          }))
          bundle.onLoad({ filter: /.*/, namespace: 'source-theme-text' }, async (args) => ({
            contents: await readFile(args.path, 'utf8'),
            loader: 'text',
          }))
        },
      },
    ],
    logLevel: 'silent',
  })
  await writeFile(
    join(probe, 'index.html'),
    '<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="probe.css"></head><body><div id="root"></div><script src="probe.js"></script></body></html>',
  )
})
test.beforeEach(() => {
  app = undefined
  rows.length = 0
  errors.length = 0
})
test.afterEach(async () => {
  try {
    await test
      .info()
      .attach('native-control-contrast', { body: JSON.stringify(rows), contentType: 'application/json' })
    if (page && !page.isClosed() && test.info().status !== test.info().expectedStatus)
      await page.screenshot({ path: test.info().outputPath('before-teardown.png') })
  } finally {
    await closeFixture(app)
  }
  expect(errors).toEqual([])
})
async function start(palette: string, kind: string, width: number, height: number) {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-theme-contrast-'))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/theme-contrast-main.mjs'),
      '--data-dir',
      directory,
      join(probe, 'index.html'),
      palette,
      kind,
      '--force-color-profile=srgb',
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await app.evaluate(
    ({ BrowserWindow }, size) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setMinimumSize(0, 0)
      window.setContentSize(size.width, size.height)
      window.focus()
    },
    { width, height },
  )
  await expect(page.locator('.avalon-shell')).toBeVisible()
  await page.evaluate(() => document.fonts.ready)
}
async function configure(patch: { palette?: string; kind?: string; disabled?: boolean; current?: boolean }) {
  await page.evaluate((patch) => (window as any).themeContrastProbe.configure(patch), patch)
  await frame()
}
async function frame() {
  await page.evaluate(
    () => new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done()))),
  )
}
async function clear() {
  await page.mouse.move(2, 2)
  await page.evaluate(() => (document.activeElement as HTMLElement)?.blur())
  await frame()
}
async function keyboardFocus(target: Locator) {
  await target.focus()
  await page.keyboard.press('Shift+Tab')
  await page.keyboard.press('Tab')
  await expect(target).toBeFocused()
  expect(await target.evaluate((node) => node.matches(':focus-visible'))).toBe(true)
  await frame()
}
async function measure(target: Locator, label: string) {
  const value = await target.evaluate((element) => {
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = 1
    const context = canvas.getContext('2d', { willReadFrequently: true })!
    const color = (css: string) => {
      context.clearRect(0, 0, 1, 1)
      context.fillStyle = css
      context.fillRect(0, 0, 1, 1)
      return [...context.getImageData(0, 0, 1, 1).data]
    }
    const over = (front: number[], back: number[]) =>
      [0, 1, 2]
        .map((i) => Math.round((front[i]! * front[3]!) / 255 + back[i]! * (1 - front[3]! / 255)))
        .concat(255)
    const luminance = (rgba: number[]) =>
      rgba
        .slice(0, 3)
        .map((value) => {
          const c = value / 255
          return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)
        })
        .reduce((sum, value, i) => sum + value * [0.2126, 0.7152, 0.0722][i]!, 0)
    const ratio = (foreground: number[], background: number[]) => {
      const a = luminance(over(foreground, background)),
        b = luminance(background)
      return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
    }
    const ancestors: Element[] = []
    for (let node: Element | null = element; node; node = node.parentElement) ancestors.unshift(node)
    let fill = [255, 255, 255, 255]
    let surrounding = fill
    const layers = ancestors.map((node) => {
      const style = getComputedStyle(node),
        background = color(style.backgroundColor)
      if (node === element) surrounding = fill
      fill = over(background, fill)
      return {
        tag: node.tagName,
        classes: node.className,
        background: style.backgroundColor,
        opacity: style.opacity,
      }
    })
    const style = getComputedStyle(element)
    const token = (name: string) => {
      const sample = document.createElement('span')
      sample.style.color = `var(${name})`
      element.append(sample)
      const value = color(getComputedStyle(sample).color)
      sample.remove()
      return value
    }
    const glyph = element.querySelector('svg path')
    const glyphStyle = glyph ? getComputedStyle(glyph) : undefined
    const ink = color(style.color),
      border = color(style.borderBottomColor),
      outline = color(style.outlineColor)
    const glyphInk = glyphStyle
      ? color(glyphStyle.stroke === 'none' ? glyphStyle.color : glyphStyle.stroke)
      : null
    return {
      textContrast: ratio(ink, fill),
      borderContrast: ratio(border, fill),
      outlineContrast: ratio(outline, fill),
      outlineSurroundingContrast: ratio(outline, surrounding),
      surrounding,
      glyphContrast: glyphInk ? ratio(glyphInk, fill) : null,
      fill,
      fillLuminance: luminance(fill),
      ink,
      border,
      outline,
      fontSize: parseFloat(style.fontSize),
      fontWeight: Number(style.fontWeight),
      tokens: { text: token('--text'), muted: token('--muted'), accent: token('--accent-foreground') },
      colorScheme: style.colorScheme,
      borderWidth: parseFloat(style.borderBottomWidth),
      outlineWidth: parseFloat(style.outlineWidth),
      outlineOffset: parseFloat(style.outlineOffset),
      outlineStyle: style.outlineStyle,
      opacity: Number(style.opacity),
      disabled: (element as HTMLButtonElement).disabled,
      hover: element.matches(':hover'),
      active: element.matches(':active'),
      focusVisible: element.matches(':focus-visible'),
      layers,
    }
  })
  rows.push({ label, ...value })
  return value
}
function readable(value: Awaited<ReturnType<typeof measure>>, label: string) {
  expect(value.textContrast, `${label}: text contrast`).toBeGreaterThanOrEqual(4.5)
}
async function buttonStates(target: Locator, label: string, quiet = false, fullscreen = false) {
  const check = async (state: string) => {
    const value = await measure(target, `${label}/${state}`)
    readable(value, `${label}/${state}`)
    if (fullscreen) {
      expect(value.fontSize).toBeCloseTo(28, 1)
      if (['pressed', 'focus', 'current', 'current-focus'].includes(state)) {
        expect(value.borderWidth).toBeGreaterThan(0)
        expect(value.borderContrast, `${label}/${state}: underline contrast`).toBeGreaterThanOrEqual(3)
      }
    } else if (quiet && state !== 'focus') {
      expect(value.borderWidth).toBeGreaterThan(0)
      expect(value.borderContrast, `${label}/${state}: quiet boundary`).toBeGreaterThanOrEqual(3)
    } else if (state === 'focus') {
      // CSS paints this ring outside the button, across the parent surface; Avalonia used an inner border.
      expect(value.outlineWidth).toBeGreaterThan(0)
      expect(value.outlineOffset).toBeGreaterThan(0)
      expect(value.outlineStyle).toBe('solid')
      expect(
        value.outlineSurroundingContrast,
        `${label}/${state}: external keyboard focus`,
      ).toBeGreaterThanOrEqual(3)
    }
    return value
  }
  await clear()
  await check('normal')
  await target.hover()
  await frame()
  expect((await check('hover')).hover).toBe(true)
  await page.mouse.down()
  await frame()
  expect((await check('pressed')).active).toBe(true)
  await page.mouse.move(2, 2)
  await page.mouse.up()
  await keyboardFocus(target)
  await check('focus')
  await page.screenshot({ path: test.info().outputPath(`${label.replaceAll('/', '-')}-focus.png`) })
  if (fullscreen) {
    await clear()
    await configure({ current: true })
    await expect(target).toHaveAttribute('aria-current', 'true')
    const current = await check('current')
    expect(current.fontWeight).toBe(700)
    expect(current.ink).toEqual(current.tokens.text)
    expect(current.border).toEqual(current.tokens.muted)
    await page.screenshot({ path: test.info().outputPath(`${label.replaceAll('/', '-')}-current.png`) })
    await keyboardFocus(target)
    const currentFocus = await check('current-focus')
    expect(currentFocus.fontWeight).toBe(700)
    expect(currentFocus.ink).toEqual(currentFocus.tokens.accent)
    expect(currentFocus.border).toEqual(currentFocus.tokens.accent)
  }
  await configure({ disabled: true })
  await expect(target).toBeDisabled()
  const disabled = await measure(target, `${label}/disabled-supplement`)
  expect(disabled.disabled).toBe(true)
  await configure({ disabled: false })
}
for (const palette of ['rose-pine-dawn', 'silkcircuit-dawn']) {
  test(`desktop ${palette} actual action and destructive templates retain contrast across pointer keyboard and disabled states`, async () => {
    await start(palette, 'actions', 760, 340)
    await buttonStates(page.getByRole('button', { name: 'Save changes', exact: true }), `${palette}/primary`)
    await buttonStates(page.getByRole('button', { name: 'Cancel', exact: true }), `${palette}/quiet`, true)
    await app!.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.setContentSize(760, 480))
    await configure({ kind: 'destructive' })
    const prompt = page.getByRole('dialog', { name: 'Delete this list?', exact: true })
    await expect(prompt).toBeVisible()
    await buttonStates(
      prompt.getByRole('button', { name: 'Delete list', exact: true }),
      `${palette}/real-destructive`,
    )
  })
  test(`fullscreen ${palette} real action keeps readable labels and a contrasting pressed focus and current underline`, async () => {
    await start(palette, 'fullscreen', 1920, 1080)
    await buttonStates(
      page.getByRole('button', { name: 'Open game', exact: true }),
      `${palette}/fullscreen-action`,
      false,
      true,
    )
  })
}
test('selected segments and real Steam chip retain Dawn text hover glyph and focus contrast', async () => {
  await start('rose-pine-dawn', 'selected', 600, 200)
  for (const palette of ['rose-pine-dawn', 'silkcircuit-dawn']) {
    await configure({ palette })
    await clear()
    readable(
      await measure(
        page.getByRole('button', { name: 'Tracked sessions', exact: true }),
        `${palette}/selected`,
      ),
      'selected segment',
    )
    const chip = page.getByRole('button', { name: 'Remove Steam filter', exact: true })
    const normal = await measure(chip, `${palette}/chip-normal`)
    await chip.hover()
    await frame()
    const hover = await measure(chip, `${palette}/chip-hover`)
    expect(hover.fill).not.toEqual(normal.fill)
    expect(hover.glyphContrast).toBeGreaterThanOrEqual(3)
    await keyboardFocus(chip)
    const focus = await measure(chip, `${palette}/chip-hover-focus`)
    expect(focus.outlineWidth).toBeGreaterThan(0)
    expect(focus.outlineContrast).toBeGreaterThanOrEqual(3)
    await page.screenshot({ path: test.info().outputPath(`${palette}-selected-controls.png`) })
  }
})
test('retained native input and select follow dark Dawn Dawn dark without stale rendered colors', async () => {
  await start('winnow', 'inputs', 420, 260)
  for (const palette of ['winnow', 'rose-pine-dawn', 'silkcircuit-dawn', 'winnow']) {
    await configure({ palette })
    await clear()
    const input = await measure(
      page.getByRole('textbox', { name: 'Library search', exact: true }),
      `${palette}/input`,
    )
    const select = await measure(
      page.getByRole('combobox', { name: 'Library sort', exact: true }),
      `${palette}/select`,
    )
    const light = palette !== 'winnow'
    expect(input.colorScheme).toBe(light ? 'light' : 'dark')
    if (light) expect(input.fillLuminance).toBeGreaterThan(0.5)
    else expect(input.fillLuminance).toBeLessThan(0.2)
    readable(input, `${palette}/input`)
    readable(select, `${palette}/select`)
    await page.screenshot({ path: test.info().outputPath(`${palette}-native-controls.png`) })
  }
})

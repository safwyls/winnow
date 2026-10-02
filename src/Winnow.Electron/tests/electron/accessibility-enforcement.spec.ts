import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import electronPath from 'electron'
import { access, mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'
import { prebuiltActivationHelper, prebuiltBackend } from './prebuilt-backend'
import { assertAccessibleControls } from './controller-accessibility-helpers'
import { collectionChoice, selectCollection } from './collection-controls'
import { returnToLibrary } from './library-controls'
import type { ApiRequest } from '../../src/shared/bridge'
import type { GameList, LibraryResponse } from '../../src/renderer/api/types'

// Production application, named bridge, HTTP API and throwaway SQLite. Only the
// standard Gamepad API and OS media preference are simulated by the native harness.
let app: ElectronApplication | undefined, page: Page, directory: string
const errors: string[] = []
const evidence: Record<string, unknown> = {}
test.beforeAll(async () => {
  await Promise.all([access(prebuiltBackend), access(prebuiltActivationHelper)])
})
test.beforeEach(async () => {
  app = undefined
  errors.length = 0
  for (const key of Object.keys(evidence)) delete evidence[key]
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-accessibility-enforcement-'))
  const palette = JSON.parse(
    await readFile(resolve('src/renderer/themes/avalon/assets/rose-pine-dawn.json'), 'utf8'),
  )
  palette.id = 'native-accessibility'
  palette.name = 'Native accessibility Dawn'
  await mkdir(join(directory, 'themes'))
  await writeFile(join(directory, 'themes/native-accessibility.json'), JSON.stringify(palette))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync', '--force-color-profile=srgb'],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: prebuiltBackend,
      WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
    },
    chromiumSandbox: true,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-shell')).toBeVisible({ timeout: 30000 })
  if (await page.getByRole('button', { name: 'Skip setup', exact: true }).isVisible())
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
})
test.afterEach(async ({}, info) => {
  let observer: ReturnType<typeof setInterval> | undefined
  try {
    if (page && !page.isClosed()) await page.screenshot({ path: info.outputPath('before-teardown.png') })
    if (app) {
      const launcher = app.process()
      const owned = await app.evaluate(() => ({
        main: process.pid,
        children: (process as NodeJS.Process & { _getActiveHandles(): { pid?: number }[] })
          ._getActiveHandles()
          .flatMap((handle) => (handle.pid ? [handle.pid] : [])),
      }))
      const lifetime: unknown[] = []
      const start = Date.now()
      observer = setInterval(
        () =>
          lifetime.push({
            elapsed: Date.now() - start,
            processes: [owned.main, ...owned.children].map((pid) => {
              try {
                process.kill(pid, 0)
                return { pid, exists: true }
              } catch {
                return { pid, exists: false }
              }
            }),
            wrapper: {
              pid: launcher.pid,
              exit: launcher.exitCode,
              signal: launcher.signalCode,
            },
          }),
        100,
      )
      evidence.lifecycle = { owned, lifetime }
    }
  } finally {
    try {
      await closeFixture(app, directory)
    } finally {
      clearInterval(observer)
      await writeFile(info.outputPath('native-metrics.json'), JSON.stringify(evidence, null, 2))
    }
  }
  expect(errors).toEqual([])
})
async function mode(fullscreen: boolean) {
  await app!.evaluate(({ BrowserWindow }, fullscreen) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setMinimumSize(0, 0)
    window.setContentSize(fullscreen ? 1920 : 1200, fullscreen ? 1080 : 800)
    window.isFullScreen = () => fullscreen
    window.webContents.send('winnow:fullscreen:changed', fullscreen)
    window.focus()
  }, fullscreen)
  await expect(page.locator(`.avalon-shell.${fullscreen ? 'fullscreen' : 'desktop'}`)).toBeVisible()
  await page.evaluate(
    () => new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done()))),
  )
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
}
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}
async function navigate(name: string) {
  await (name === 'Settings' ? page : page.getByRole('navigation', { name: 'Main navigation' }))
    .getByRole('button', { name, exact: true })
    .click()
}
async function authored() {
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await page.getByRole('combobox', { name: /^Avalon palette/ }).selectOption('native-accessibility')
  await expect(page.locator('html')).toHaveCSS('color-scheme', 'light')
  const numericValue = page
    .getByRole('slider', { name: 'Theme text size', exact: true })
    .locator('..')
    .locator('.studio-value')
  await page.getByRole('combobox', { name: 'Data font', exact: true }).fill('Plus Jakarta Sans')
  await page.getByRole('combobox', { name: 'Data font', exact: true }).press('Tab')
  await expect(numericValue).toHaveCSS('font-family', /Avalon Body/)
  await expect(numericValue).toHaveCSS('font-variant-numeric', 'tabular-nums')
  evidence.liveProportionalData = await numericValue.evaluate((node) => ({
    family: getComputedStyle(node).fontFamily,
    numeric: getComputedStyle(node).fontVariantNumeric,
    text: node.textContent,
  }))
  await page.getByRole('combobox', { name: 'Data font', exact: true }).fill('IBM Plex Mono')
  await page.getByRole('combobox', { name: 'Data font', exact: true }).press('Tab')
  await expect
    .poll(() =>
      page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--font-mono')),
    )
    .toContain('Avalon Data')
  await expect(numericValue).toHaveCSS('font-family', /Avalon Data/)
  await expect(numericValue).toHaveCSS('font-variant-numeric', 'tabular-nums')
  evidence.liveBundledData = await renderedFonts(numericValue)
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
}
async function ax(locator: Locator) {
  const marker = `native-ax-${Date.now()}-${Math.random().toString(36).slice(2)}`
  await locator.evaluate((node, id) => node.setAttribute('data-native-ax', id), marker)
  const session = await page.context().newCDPSession(page)
  try {
    await session.send('Accessibility.enable')
    const { root } = await session.send('DOM.getDocument')
    const { nodeId } = await session.send('DOM.querySelector', {
      nodeId: root.nodeId,
      selector: `[data-native-ax="${marker}"]`,
    })
    const { node } = await session.send('DOM.describeNode', { nodeId })
    const { nodes } = await session.send('Accessibility.getPartialAXTree', {
      backendNodeId: node.backendNodeId,
      fetchRelatives: true,
    })
    const own = nodes.find((entry) => entry.backendDOMNodeId === node.backendNodeId)!
    expect(own).toBeDefined()
    return { own, descendants: nodes.filter((entry) => entry !== own) }
  } finally {
    await session.send('Accessibility.disable')
    await session.send('DOM.disable')
    await session.detach()
  }
}
async function named(locator: Locator, name: string) {
  await expect(locator).toHaveAccessibleName(name)
  const tree = await ax(locator)
  expect(tree.own.ignored).toBe(false)
  expect(tree.own.name?.value).toBe(name)
  return tree
}
async function renderedFonts(locator: Locator) {
  await locator.evaluate((node) => node.setAttribute('data-native-font-probe', 'true'))
  const session = await page.context().newCDPSession(page)
  try {
    await session.send('DOM.enable')
    await session.send('CSS.enable')
    const { root } = await session.send('DOM.getDocument')
    const { nodeId } = await session.send('DOM.querySelector', {
      nodeId: root.nodeId,
      selector: '[data-native-font-probe="true"]',
    })
    return (await session.send('CSS.getPlatformFontsForNode', { nodeId })).fonts
  } finally {
    await session.send('CSS.disable')
    await session.send('DOM.disable')
    await session.detach()
    await locator.evaluate((node) => node.removeAttribute('data-native-font-probe'))
  }
}
async function liveCount(count: number) {
  const live = page.locator('.avalon-library .avalon-results-count').first()
  await expect(live).toHaveText(`${count} ${count === 1 ? 'game' : 'games'}`)
  const tree = await ax(live)
  expect(tree.own.ignored).toBe(false)
  expect(tree.own.properties?.find((item) => item.name === 'live')?.value.value).toBe('polite')
  const text = tree.descendants
    .filter((node) => node.parentId === tree.own.nodeId && node.role?.value === 'StaticText')
    .map((node) => node.name?.value ?? '')
    .join('')
  expect(text).toBe(`${count} ${count === 1 ? 'game' : 'games'}`)
  return tree
}
async function announcement(status: Locator, text: string) {
  await expect(status).toHaveText(text)
  const tree = await ax(status)
  expect(tree.own.ignored).toBe(false)
  expect(tree.own.role?.value).toBe('status')
  expect(tree.own.properties?.find((item) => item.name === 'live')?.value.value).toBe('polite')
  expect(tree.own.properties?.find((item) => item.name === 'atomic')?.value.value).toBe(true)
  expect(JSON.stringify(tree)).toContain(text)
  return tree
}
async function openFilters(fullscreen: boolean) {
  await returnToLibrary(page)
  await page.getByRole('button', { name: fullscreen ? 'Filter & sort' : 'Filters', exact: true }).click()
  const panel = page.getByRole(fullscreen ? 'dialog' : 'region', { name: 'Library filters', exact: true })
  if (fullscreen) await panel.getByRole('button', { name: /^PLATFORM/ }).click()
  else
    await panel
      .locator('summary')
      .filter({ hasText: /^Stores$/ })
      .click()
  return panel
}
async function closeFilters(fullscreen: boolean) {
  const panel = page.getByRole(fullscreen ? 'dialog' : 'region', { name: 'Library filters', exact: true })
  if (fullscreen) await page.keyboard.press('Escape')
  else await panel.getByRole('button', { name: 'Close filters', exact: true }).click()
  // Fullscreen Escape first leaves the choices page, then closes the filter owner.
  if (await panel.isVisible()) await page.keyboard.press('Escape')
  await expect(panel).toHaveCount(0)
}
for (const surface of ['desktop', 'fullscreen'] as const) {
  test(`${surface} exposes outer card peers and live exact list and filter names through real API changes`, async () => {
    test.setTimeout(120000)
    const fullscreen = surface === 'fullscreen'
    await mode(fullscreen)
    await authored()
    const feed = page.locator('.avalon-cover').first()
    await expect(feed).toBeVisible()
    const feedName = await feed.getAttribute('aria-label')
    const feedAX = await named(feed, feedName!)
    expect(feedAX.own.role?.value).toBe('button')
    await feed.focus()
    await expect(feed).toBeFocused()
    evidence.feed = feedAX
    await navigate('Library')
    const snapshot = await api<LibraryResponse>({ route: 'library.get' })
    const steam = snapshot.games
      .filter((game) => game.entries.some((entry) => entry.store === 'steam'))
      .slice(0, 2)
    expect(steam).toHaveLength(2)
    const ids = steam.map((game) => game.entries.find((entry) => entry.store === 'steam')!.releaseId)
    let list = await api<GameList>({ route: 'list.create', body: { name: 'Co-op', releaseIds: ids } })
    const listNode = await collectionChoice(page, list.id)
    evidence.listTwo = await named(listNode, 'Co-op, 2 games')
    const listStatus = listNode.locator('xpath=following-sibling::*[1][@role="status"]')
    await expect(listStatus).toHaveText('')
    const listHandle = await listNode.elementHandle(),
      statusHandle = await listStatus.elementHandle()
    list = await api<GameList>({
      route: 'list.update',
      params: { listId: list.id },
      body: { name: 'Weekend', description: null, expectedRevision: list.revision },
    })
    evidence.listRenamed = await named(listNode, 'Weekend, 2 games')
    evidence.listRenameAnnouncement = await announcement(listStatus, 'Weekend, 2 games')
    list = await api<GameList>({
      route: 'list.member.remove',
      params: { listId: list.id },
      body: { releaseIds: [ids[1]], expectedRevision: list.revision },
    })
    evidence.listOne = await named(listNode, 'Weekend, 1 game')
    evidence.listCountAnnouncement = await announcement(listStatus, 'Weekend, 1 game')
    expect(await listHandle!.evaluate((node) => node.isConnected)).toBe(true)
    expect(await statusHandle!.evaluate((node) => node.isConnected)).toBe(true)
    await returnToLibrary(page)
    await selectCollection(page, list.id)
    evidence.liveOne = await liveCount(1)
    const tile = page.locator('.avalon-library .avalon-cover').first()
    const tileName = await tile.getAttribute('aria-label')
    const tileAX = await named(tile, tileName!)
    expect(tileAX.own.role?.value).toBe('button')
    await tile.focus()
    await tile.press('Tab')
    await page.keyboard.press('Shift+Tab')
    await expect(tile).toBeFocused()
    evidence.tile = tileAX
    const decorative = tile.locator('.avalon-cover-fallback')
    evidence.decorative = await ax(decorative)
    expect((evidence.decorative as Awaited<ReturnType<typeof ax>>).own.ignored).toBe(true)
    // Chromium's generic element is not an interactive peer even if aria-label is supplied.
    await page.evaluate(() => {
      const node = document.createElement('div')
      node.id = 'native-generic-peer'
      node.setAttribute('aria-label', 'Decorative border fixture')
      document.body.append(node)
    })
    const generic = await ax(page.locator('#native-generic-peer'))
    evidence.genericNegativeControl = generic
    expect(generic.own.role?.value).not.toBe('button')
    expect(
      generic.own.properties?.some((item) => item.name === 'focusable' && item.value.value === true),
    ).toBe(false)
    await page.locator('#native-generic-peer').evaluate((node) => node.remove())
    await page.screenshot({ path: test.info().outputPath(`${surface}-named-library.png`) })
    const panel = await openFilters(fullscreen)
    const steamChoice = panel.getByRole(fullscreen ? 'button' : 'checkbox', {
      name: /^Steam, \d+ matching titles?$/,
    })
    const steamStatus = (fullscreen ? steamChoice : steamChoice.locator('..')).locator(
      'xpath=following-sibling::*[1][@role="status"]',
    )
    await expect(steamStatus).toHaveText('')
    const steamHandle = await steamChoice.elementHandle(),
      steamStatusHandle = await steamStatus.elementHandle()
    evidence.filterOne = await named(
      panel.getByRole(fullscreen ? 'button' : 'checkbox', { name: 'Steam, 1 matching title', exact: true }),
      'Steam, 1 matching title',
    )
    expect(
      await panel
        .getByRole(fullscreen ? 'button' : 'checkbox', { name: /games with updates|has updates/i })
        .count(),
    ).toBe(0)
    evidence.filterControls = await assertAccessibleControls(page, panel)
    list = await api<GameList>({
      route: 'list.member.add',
      params: { listId: list.id },
      body: { releaseIds: [ids[1]], expectedRevision: list.revision },
    })
    evidence.filterTwo = await named(
      panel.getByRole(fullscreen ? 'button' : 'checkbox', { name: 'Steam, 2 matching titles', exact: true }),
      'Steam, 2 matching titles',
    )
    evidence.filterAnnouncement = await announcement(steamStatus, 'Steam, 2 matching titles')
    expect(await steamHandle!.evaluate((node) => node.isConnected)).toBe(true)
    expect(await steamStatusHandle!.evaluate((node) => node.isConnected)).toBe(true)
    await page.screenshot({ path: test.info().outputPath(`${surface}-live-filter-two.png`) })
    await closeFilters(fullscreen)
    evidence.liveTwo = await liveCount(2)
    await selectCollection(page, 'all')
    const patched = page.locator('.avalon-buckets button').filter({ hasText: 'Patched' }).first()
    await patched.click()
    const badges = page.locator('.avalon-library .avalon-unread')
    await expect(badges.first()).toBeVisible()
    evidence.badgeDeclarations = await page.evaluate(() => {
      const declarations: {
        selector: string
        left: string
        top: string
        borderLeft: string
        borderTop: string
        border: string
      }[] = []
      const visit = (rules: CSSRuleList) => {
        for (const rule of rules) {
          if (
            rule instanceof CSSStyleRule &&
            rule.selectorText
              .split(',')
              .some((part) => ['.avalon-cover', '.avalon-unread'].includes(part.trim()))
          )
            declarations.push({
              selector: rule.selectorText,
              left: rule.style.left,
              top: rule.style.top,
              borderLeft: rule.style.borderLeftWidth,
              borderTop: rule.style.borderTopWidth,
              border: rule.style.border,
            })
          if ('cssRules' in rule) visit((rule as CSSGroupingRule).cssRules)
        }
      }
      for (const sheet of document.styleSheets) visit(sheet.cssRules)
      return declarations
    })
    expect(evidence.badgeDeclarations).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ left: '7px', top: '7px' }),
        expect.objectContaining({ border: '1px solid var(--line)' }),
      ]),
    )
    evidence.badges = await badges.evaluateAll((nodes) =>
      nodes.map((node) => {
        const cover = node.closest('.avalon-cover')!,
          box = node.getBoundingClientRect(),
          parent = cover.getBoundingClientRect(),
          style = getComputedStyle(node),
          parentStyle = getComputedStyle(cover)
        const zoom = Number(getComputedStyle(document.body).zoom) || 1
        return {
          name: cover.getAttribute('aria-label'),
          x: (box.x - parent.x) / zoom,
          y: (box.y - parent.y) / zoom,
          physicalX: box.x - parent.x,
          physicalY: box.y - parent.y,
          insetX: parseFloat(style.left),
          insetY: parseFloat(style.top),
          borderX: parseFloat(parentStyle.borderLeftWidth),
          borderY: parseFloat(parentStyle.borderTopWidth),
          zoom,
          fill: style.backgroundColor,
          flare: getComputedStyle(document.documentElement).getPropertyValue('--avalon-flare').trim(),
        }
      }),
    )
    for (const badge of evidence.badges as {
      name: string
      physicalX: number
      physicalY: number
      insetX: number
      insetY: number
      borderX: number
      borderY: number
      zoom: number
    }[]) {
      expect(badge.name).toMatch(/patch|update/i)
      expect(badge.insetX).toBe(7)
      expect(badge.insetY).toBe(7)
      // Source Margin=8 is logical: the production 7px inset plus 1px border.
      // At body zoom .85 Chromium rounds the border to a device pixel; compare
      // painted bounds to that computed border, within one layout quantum.
      expect(Math.abs(badge.physicalX - (badge.insetX + badge.borderX) * badge.zoom)).toBeLessThanOrEqual(
        1 / 64 + 1e-6,
      )
      expect(Math.abs(badge.physicalY - (badge.insetY + badge.borderY) * badge.zoom)).toBeLessThanOrEqual(
        1 / 64 + 1e-6,
      )
    }
    const numbers = page.locator(fullscreen ? '.avalon-clock' : '.avalon-buckets small')
    await expect(numbers.first()).toBeVisible()
    evidence.numeric = await numbers.first().evaluate(async (node) => {
      await document.fonts.ready
      const style = getComputedStyle(node),
        canvas = document.createElement('canvas'),
        context = canvas.getContext('2d')!
      context.font = `${style.fontSize} ${style.fontFamily}`
      return {
        family: style.fontFamily,
        variant: style.fontVariantNumeric,
        widths: ['1111', '8888', '0000'].map((text) => context.measureText(text).width),
      }
    })
    const numeric = evidence.numeric as { family: string; variant: string; widths: number[] }
    expect(numeric.family).toContain('Avalon Data')
    expect(numeric.variant).toContain('tabular-nums')
    expect(numeric.widths[0]).toBeCloseTo(numeric.widths[1], 3)
    expect(numeric.widths[0]).toBeCloseTo(numeric.widths[2], 3)
    const glyphFonts = await renderedFonts(numbers.first())
    evidence.renderedDataFonts = glyphFonts
    expect(
      glyphFonts.some(
        (font) => font.isCustomFont && /IBM Plex Mono/i.test(font.familyName) && font.glyphCount > 0,
      ),
    ).toBe(true)
    await page.screenshot({ path: test.info().outputPath(`${surface}-unread-data-role.png`) })
  })
}

async function motionSnapshot(scope: Locator) {
  return scope.evaluate((root) => {
    const visible = [root, ...root.querySelectorAll('*')].filter(
      (node) => node.getBoundingClientRect().width > 0,
    )
    return {
      moving: visible.flatMap((node) =>
        ['', '::before', '::after'].flatMap((pseudo) => {
          const s = getComputedStyle(node, pseudo || null)
          const moving =
            s.transitionDuration.split(',').some((value) => parseFloat(value) > 0) ||
            (s.animationName !== 'none' &&
              s.animationDuration.split(',').some((value) => parseFloat(value) > 0))
          return moving
            ? [
                {
                  tag: node.tagName,
                  className: node.getAttribute('class'),
                  pseudo,
                  transition: s.transitionDuration,
                  animation: s.animationName,
                },
              ]
            : []
        }),
      ),
      animations: document.getAnimations().filter((animation) => animation.playState === 'running').length,
    }
  })
}
async function afterglowFrameProbe(label: string) {
  await page.evaluate(async () => {
    await document.fonts.ready
    const state = {
      done: false,
      frames: 0,
      samples: [] as { opacity: string; transform: string; x: number; y: number }[],
    }
    Object.assign(window, { nativeHeroFrames: state })
    const sample = () => {
      const node = document.querySelector('.hero-copy')
      if (node) {
        const style = getComputedStyle(node),
          box = node.getBoundingClientRect()
        state.samples.push({ opacity: style.opacity, transform: style.transform, x: box.x, y: box.y })
      }
      if (++state.frames < 60) requestAnimationFrame(sample)
      else state.done = true
    }
    requestAnimationFrame(sample)
  })
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  await expect(page.locator('.hero-copy')).toBeVisible()
  await expect
    .poll(() =>
      page.evaluate(
        () => (window as unknown as { nativeHeroFrames: { done: boolean } }).nativeHeroFrames.done,
      ),
    )
    .toBe(true)
  const measured = await page.evaluate(
    () =>
      (
        window as unknown as {
          nativeHeroFrames: { samples: { opacity: string; transform: string; x: number; y: number }[] }
        }
      ).nativeHeroFrames.samples,
  )
  expect(measured.length).toBeGreaterThan(3)
  for (const frame of measured) {
    expect(frame.opacity).toBe('1')
    expect(frame.transform === 'none' || frame.transform === 'matrix(1, 0, 0, 1, 0, 0)').toBe(true)
    expect(frame.x).toBeCloseTo(measured[0].x, 1)
    expect(frame.y).toBeCloseTo(measured[0].y, 1)
  }
  evidence[label] = measured
  await page.screenshot({ path: test.info().outputPath(`${label}.png`) })
}
for (const surface of ['desktop', 'fullscreen'] as const) {
  test(`${surface} authored theme suppresses live OS and saved profile motion across cards and dialogs`, async () => {
    test.setTimeout(120000)
    const fullscreen = surface === 'fullscreen'
    await mode(fullscreen)
    await authored()
    await navigate('Library')
    const tile = page.locator('.avalon-library .avalon-cover').first()
    await expect(tile).toBeVisible()
    const held = await tile.elementHandle()
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    const ordinary = await motionSnapshot(page.locator('.avalon-library'))
    expect(ordinary.moving.length).toBeGreaterThan(0)
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await expect
      .poll(() => motionSnapshot(page.locator('.avalon-library')))
      .toEqual({ moving: [], animations: 0 })
    expect(await held!.evaluate((node) => node.isConnected)).toBe(true)
    await tile.hover()
    await tile.focus()
    evidence.osReduced = await motionSnapshot(page.locator('.avalon-library'))
    expect(evidence.osReduced).toEqual({ moving: [], animations: 0 })
    await tile.press('Enter')
    await expect(page.locator('.avalon-details')).toBeVisible()
    expect(await motionSnapshot(page.locator('.avalon-details'))).toEqual({ moving: [], animations: 0 })
    await page.screenshot({ path: test.info().outputPath(`${surface}-os-reduced-details.png`) })
    await page.keyboard.press('Escape')
    await expect(page.locator('.avalon-details')).toHaveCount(0)
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    await expect
      .poll(async () => (await motionSnapshot(page.locator('.avalon-library'))).moving.length)
      .toBeGreaterThan(0)
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    await page.getByRole('checkbox', { name: /^Reduce motion/ }).check()
    await expect(page.locator('html')).toHaveClass(/reduced-motion/)
    await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
    await expect(page.locator('.avalon-cover').first()).toBeVisible()
    expect(await motionSnapshot(page.locator('.avalon-shell'))).toEqual({ moving: [], animations: 0 })
    await page.reload()
    await expect(page.locator('.avalon-shell')).toBeVisible()
    await mode(fullscreen)
    await expect(page.locator('html')).toHaveClass(/reduced-motion/)
    await expect(page.locator('html')).toHaveCSS('color-scheme', 'light')
    evidence.persistedProfile = await page.evaluate(() => window.winnow.loadPreferences())
    evidence.savedReduced = await motionSnapshot(page.locator('.avalon-shell'))
    expect(evidence.savedReduced).toEqual({ moving: [], animations: 0 })
    await page.screenshot({ path: test.info().outputPath(`${surface}-authored-saved-reduced.png`) })
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    await page.getByRole('checkbox', { name: /^Reduce motion/ }).uncheck()
    await expect(page.locator('html')).not.toHaveClass(/reduced-motion/)
    await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
    await expect
      .poll(async () => (await motionSnapshot(page.locator('.avalon-shell'))).moving.length)
      .toBeGreaterThan(0)
    if (fullscreen) {
      await api({
        route: 'preferences.presentation.put',
        params: { preference: 'FullscreenReducedMotion' },
        body: { value: 'true' },
      })
      await expect(page.locator('html')).toHaveClass(/reduced-motion/)
      await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
      await expect(page.getByRole('checkbox', { name: /^Reduce motion/ })).not.toBeChecked()
      await page.getByRole('combobox', { name: /^Avalon palette/ }).selectOption('winnow')
      await page.getByRole('combobox', { name: 'Interface font', exact: true }).fill('IBM Plex Mono')
      await page.getByRole('combobox', { name: 'Interface font', exact: true }).press('Tab')
      await expect(page.locator('html')).toHaveClass(/reduced-motion/)
      await page.getByRole('combobox', { name: /^Avalon palette/ }).selectOption('native-accessibility')
      await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
      evidence.fullscreenSavedAfterThemeEdit = await motionSnapshot(page.locator('.avalon-shell'))
      expect(evidence.fullscreenSavedAfterThemeEdit).toEqual({ moving: [], animations: 0 })
      await page.screenshot({ path: test.info().outputPath('fullscreen-saved-motion-after-theme-edit.png') })
      await api({
        route: 'preferences.presentation.put',
        params: { preference: 'FullscreenReducedMotion' },
        body: { value: 'false' },
      })
    }
    evidence.ordinary = ordinary
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    await page
      .locator('.studio-theme-option')
      .filter({ has: page.getByText('Afterglow', { exact: true }) })
      .click()
    await afterglowFrameProbe(`${surface}-afterglow-os-frames`)
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    await page.getByRole('checkbox', { name: /^Reduce motion/ }).check()
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    await afterglowFrameProbe(`${surface}-afterglow-profile-frames`)
  })
}

async function connect(connected: boolean) {
  await page.evaluate((connected) => {
    const state = { connected, pressed: [] as number[] }
    Object.assign(window, { nativeAccessibilityPad: state })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () =>
        state.connected
          ? [
              {
                id: 'Xbox 360 Controller (XInput STANDARD GAMEPAD)',
                index: 0,
                mapping: 'standard',
                connected: true,
                axes: [0, 0, 0, 0],
                buttons: Array.from({ length: 17 }, (_, i) => ({
                  pressed: state.pressed.includes(i),
                  value: state.pressed.includes(i) ? 1 : 0,
                })),
              },
            ]
          : [],
    })
    window.dispatchEvent(new Event(connected ? 'gamepadconnected' : 'gamepaddisconnected'))
  }, connected)
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(
        window as unknown as { nativeAccessibilityPad: { pressed: number[] } }
      ).nativeAccessibilityPad.pressed = pressed
      await new Promise<void>((resolve) =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
      )
    }, pressed)
}
test('fullscreen root guidance follows controller connection and keyboard return with main bumper hints retained', async () => {
  await mode(true)
  await authored()
  await connect(false)
  await navigate('Library')
  const footer = page.locator('.avalon-footer')
  const clock = page.locator('.avalon-clock')
  const clockAX = await ax(clock)
  expect(clockAX.own.ignored).toBe(false)
  expect(clockAX.own.role?.value).toBe('time')
  expect(clockAX.own.name?.value).toMatch(/^Local time:/)
  evidence.clock = clockAX
  await expect(footer.getByRole('group', { name: 'Keyboard guidance', exact: true })).toBeVisible()
  await expect(footer).toContainText('Arrows')
  await connect(true)
  await tap(13)
  await expect(footer.getByRole('group', { name: 'Controller guidance', exact: true })).toBeVisible()
  await expect(footer).not.toContainText('Arrows')
  await expect(footer).toContainText('Library options')
  for (const key of ['D-pad', 'A', 'Y', 'B'])
    await expect(footer.locator(`[data-input-glyph="${key}"] svg`)).toBeVisible()
  evidence.glyphColors = await footer.locator('[data-input-glyph] svg path').evaluateAll((paths) =>
    paths.map((node) => {
      const style = getComputedStyle(node)
      const footer = node.closest('.avalon-footer')!
      const rgb = (value: string) => value.match(/[\d.]+/g)!.map(Number)
      let background = [255, 255, 255]
      const ancestors: Element[] = []
      for (let parent: Element | null = footer; parent; parent = parent.parentElement)
        ancestors.unshift(parent)
      for (const parent of ancestors) {
        const value = rgb(getComputedStyle(parent).backgroundColor),
          alpha = value[3] ?? 1
        background = background.map((channel, index) =>
          Math.round(value[index] * alpha + channel * (1 - alpha)),
        )
      }
      const luminance = (value: number[]) =>
        value
          .slice(0, 3)
          .map((channel) => channel / 255)
          .map((channel) => (channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4))
          .reduce((sum, channel, index) => sum + channel * [0.2126, 0.7152, 0.0722][index], 0)
      const foreground = luminance(rgb(style.fill)),
        ground = luminance(background)
      return {
        fill: style.fill,
        color: style.color,
        background,
        contrast: (Math.max(foreground, ground) + 0.05) / (Math.min(foreground, ground) + 0.05),
      }
    }),
  )
  expect((evidence.glyphColors as unknown[]).length).toBeGreaterThan(0)
  for (const path of evidence.glyphColors as { fill: string; color: string; contrast: number }[]) {
    expect(path.fill).toBe(path.color)
    expect(path.fill).not.toBe('rgb(255, 255, 255)')
    expect(path.contrast).toBeGreaterThanOrEqual(3)
  }
  await tap(3)
  const options = page.getByRole('dialog', { name: 'Library options', exact: true })
  await expect(options).toBeVisible()
  await tap(1)
  await expect(options).toHaveCount(0)
  evidence.controllerFooter = await ax(footer)
  await page.screenshot({ path: test.info().outputPath('fullscreen-controller-root-guidance.png') })
  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  evidence.mainNavigation = await navigation.innerHTML()
  await expect(navigation.locator('svg')).toHaveCount(2)
  await page.keyboard.press('ArrowDown')
  // Existing hint policy follows connection, not last-input modality: keyboard cannot hide available controller commands.
  await expect(footer).not.toContainText('Arrows')
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  for (const key of ['LT', 'RT'])
    await expect(footer.locator(`[data-input-glyph="${key}"] svg`)).toBeVisible()
  await expect(footer).toContainText('More')
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await page.evaluate(async () => {
    await document.fonts.ready
    await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  })
  const rootBounds = await page.evaluate(() => ({
    viewport: { width: innerWidth, height: innerHeight },
    controls: [
      ...document.querySelectorAll(
        '.avalon-footer .fullscreen-input-hint, .avalon-clock, .avalon-controller-status, .avalon-navigation',
      ),
    ].map((node) => {
      const box = node.getBoundingClientRect()
      return { text: node.textContent, x: box.x, y: box.y, right: box.right, bottom: box.bottom }
    }),
  }))
  evidence.rootBounds = rootBounds
  for (const box of rootBounds.controls) {
    expect(box.x).toBeGreaterThanOrEqual(0)
    expect(box.y).toBeGreaterThanOrEqual(0)
    expect(box.right).toBeLessThanOrEqual(rootBounds.viewport.width + 1)
    expect(box.bottom).toBeLessThanOrEqual(rootBounds.viewport.height + 1)
  }
  await page.screenshot({ path: test.info().outputPath('fullscreen-controller-home-guidance.png') })
  await connect(false)
  await expect(footer.getByRole('group', { name: 'Keyboard guidance', exact: true })).toBeVisible()
  await expect(footer).toContainText('Arrows')
  await expect(navigation.locator('svg')).toHaveCount(2)
  evidence.keyboardFooter = await ax(footer)
  await page.screenshot({ path: test.info().outputPath('fullscreen-keyboard-root-guidance.png') })
})

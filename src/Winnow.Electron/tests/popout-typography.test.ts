// @vitest-environment jsdom
import { EventEmitter } from 'node:events'
import { runInNewContext } from 'node:vm'
import { afterEach, expect, it, vi } from 'vitest'
import { DEFAULT_TYPOGRAPHY } from '../src/shared/typography'
import {
  popoutFontFaces,
  popoutTypography,
  popoutTypographyScript,
  setPopoutTypography,
  subscribePopoutTypography,
} from '../src/main/popout-typography'
import { assertTrustedRendererSender } from '../src/main/security'
import type { IpcMainInvokeEvent, WebContents } from 'electron'

function owner() {
  return Object.assign(new EventEmitter(), { isDestroyed: vi.fn(() => false) })
}
afterEach(() => {
  document.body.replaceChildren()
  document.documentElement.removeAttribute('style')
})

it.each(['remote owner', 'child frame', 'remote navigation', 'lookalike URL', 'missing owner'])(
  'the application sender boundary refuses %s before publishing popout typography',
  (kind) => {
    const application = Object.assign(owner(), { mainFrame: { url: 'winnow-app://app/index.html' } })
    const event = { sender: application, senderFrame: application.mainFrame }
    if (kind === 'remote owner') event.sender = Object.assign(owner(), { mainFrame: application.mainFrame })
    if (kind === 'child frame') event.senderFrame = { ...application.mainFrame }
    if (kind === 'remote navigation') application.mainFrame.url = 'https://store.steampowered.com/login/'
    if (kind === 'lookalike URL') application.mainFrame.url = 'winnow-app://app.evil/index.html'
    expect(() =>
      assertTrustedRendererSender(
        event as unknown as IpcMainInvokeEvent,
        kind === 'missing owner' ? undefined : (application as unknown as WebContents),
      ),
    ).toThrow('Untrusted renderer')
    expect(popoutTypography(application)).toEqual(DEFAULT_TYPOGRAPHY)
  },
)

it('accepts the owning top-level application page and its exact configured development origin', () => {
  const application = Object.assign(owner(), { mainFrame: { url: 'winnow-app://app/index.html' } })
  const event = { sender: application, senderFrame: application.mainFrame } as unknown as IpcMainInvokeEvent
  expect(() => assertTrustedRendererSender(event, application as unknown as WebContents)).not.toThrow()
  application.mainFrame.url = 'http://127.0.0.1:5173/'
  expect(() =>
    assertTrustedRendererSender(event, application as unknown as WebContents, 'http://127.0.0.1:5173/'),
  ).not.toThrow()
  expect(() =>
    assertTrustedRendererSender(event, application as unknown as WebContents, 'http://127.0.0.1:5174/'),
  ).toThrow('Untrusted renderer')
})

it.each([
  null,
  [],
  { headingFont: 'https://example.test/font.ttf' },
  { interfaceFont: 'Arial; color: red' },
  { sizePercent: 79 },
  { sizePercent: 121 },
  { sizePercent: 100.5 },
  { unrelated: true },
])('rejects malformed native typography %j before changing or notifying a local document', (value) => {
  const application = owner(),
    changed = vi.fn()
  subscribePopoutTypography(application, changed)
  expect(() => setPopoutTypography(application, value)).toThrow()
  expect(popoutTypography(application)).toEqual(DEFAULT_TYPOGRAPHY)
  expect(changed).not.toHaveBeenCalled()
})

it('isolates application owners, suppresses equal updates and releases closed subscriptions and destroyed owners', () => {
  const first = owner(),
    second = owner(),
    changed = vi.fn(),
    other = vi.fn()
  const dispose = subscribePopoutTypography(first, changed)
  subscribePopoutTypography(second, other)
  setPopoutTypography(first, { ...DEFAULT_TYPOGRAPHY, sizePercent: 120 })
  setPopoutTypography(first, { ...DEFAULT_TYPOGRAPHY, sizePercent: 120 })
  expect(changed).toHaveBeenCalledTimes(1)
  expect(other).not.toHaveBeenCalled()
  const copy = popoutTypography(first)
  copy.sizePercent = 80
  expect(popoutTypography(first).sizePercent).toBe(120)
  dispose()
  setPopoutTypography(first, { ...DEFAULT_TYPOGRAPHY, sizePercent: 80 })
  expect(changed).toHaveBeenCalledTimes(1)
  first.isDestroyed.mockReturnValue(true)
  first.emit('destroyed')
  setPopoutTypography(first, DEFAULT_TYPOGRAPHY)
  expect(popoutTypography(first)).toEqual(DEFAULT_TYPOGRAPHY)
  expect(changed).toHaveBeenCalledTimes(1)
})

it('updates the retained local document across the exact scale sequence without replacing a masked draft or focus', () => {
  const application = owner()
  const draft = document.createElement('input')
  draft.type = 'password'
  draft.value = 'A local draft 🔒'
  document.body.append(draft)
  draft.focus()
  draft.setSelectionRange(2, 7)
  const dispose = subscribePopoutTypography(application, (value) =>
    runInNewContext(popoutTypographyScript(value), { document }),
  )
  for (const sizePercent of [120, 80, 100, 120]) {
    setPopoutTypography(application, {
      ...DEFAULT_TYPOGRAPHY,
      interfaceFont: 'A "quoted" family',
      sizePercent,
    })
    expect(document.documentElement.style.getPropertyValue('--theme-text-scale')).toBe(
      String(sizePercent / 100),
    )
    expect(document.documentElement.style.getPropertyValue('--font-body')).toBe(
      '"A \\"quoted\\" family", "Avalon Body", sans-serif',
    )
    expect(document.querySelector('input')).toBe(draft)
    expect(document.activeElement).toBe(draft)
    expect(draft.value).toBe('A local draft 🔒')
    expect([draft.selectionStart, draft.selectionEnd]).toEqual([2, 7])
    expect(document.documentElement.style.getPropertyValue('--fullscreen-text-scale')).toBe('')
  }
  dispose()
})

it('embeds only the three known bundled role fonts as data resources', () => {
  expect(popoutFontFaces.match(/@font-face/g)).toHaveLength(3)
  for (const family of ['Avalon Display', 'Avalon Body', 'Avalon Data'])
    expect(popoutFontFaces).toContain(`font-family:"${family}"`)
  expect(popoutFontFaces.match(/src:url\("data:/g)).toHaveLength(3)
  expect(popoutFontFaces).not.toMatch(/https?:|file:/)
})

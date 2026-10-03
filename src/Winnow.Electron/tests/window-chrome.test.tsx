// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { applyWindowChrome, titlebarOptions } from '../src/main/window-chrome'
import { WindowChrome } from '../src/renderer/components/WindowChrome'
import { applyThemeProfile } from '../src/renderer/theming/runtime'
import { DEFAULT_PROFILE, selectThemeProfile } from '../src/shared/theme'
import { avalon } from '../src/renderer/themes/avalon'
import { afterglow } from '../src/renderer/themes/afterglow'
import { rift } from '../src/renderer/themes/rift'
import { catalogue } from '../src/renderer/themes/catalogue'
import type { WinnowBridge } from '../src/shared/bridge'

beforeEach(() => {
  window.winnow = { windowChrome: vi.fn(async () => true) } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  document.documentElement.removeAttribute('style')
  document.documentElement.removeAttribute('data-theme')
})

it.each(['win32', 'linux'])('keeps native controls over a themed drag region on %s', (platform) => {
  expect(titlebarOptions(platform)).toEqual({
    titleBarStyle: 'hidden',
    titleBarOverlay: { color: '#00000000', symbolColor: '#f1ece4', height: 32 },
  })
  const target = { isDestroyed: () => false, setTitleBarOverlay: vi.fn() }
  expect(applyWindowChrome(target, platform, { text: '#121314' })).toBe(true)
  expect(target.setTitleBarOverlay).toHaveBeenCalledWith({ symbolColor: '#121314' })
})
it('retains the standard frame on other platforms and ignores destroyed windows', () => {
  expect(titlebarOptions('darwin')).toEqual({})
  const target = { isDestroyed: () => false, setTitleBarOverlay: vi.fn() }
  expect(applyWindowChrome(target, 'darwin', { text: '#121314' })).toBe(false)
  expect(applyWindowChrome({ ...target, isDestroyed: () => true }, 'win32', { text: '#121314' })).toBe(false)
  expect(target.setTitleBarOverlay).not.toHaveBeenCalled()
})
it.each([null, [], { text: 'white' }, { text: '#ff000080' }, { text: '#123456', color: 'red' }])(
  'refuses invalid chrome input %j',
  (input) => {
    expect(() =>
      applyWindowChrome({ isDestroyed: () => false, setTitleBarOverlay: vi.fn() }, 'win32', input),
    ).toThrow()
  },
)
it.each([avalon, afterglow, rift, catalogue])(
  'updates native button contrast with $id and hides the complete strip in fullscreen',
  async (theme) => {
    const view = render(<WindowChrome fullscreen={false} />)
    await act(async () =>
      applyThemeProfile(selectThemeProfile(structuredClone(DEFAULT_PROFILE), theme.id, theme)),
    )
    await waitFor(() =>
      expect(window.winnow.windowChrome).toHaveBeenLastCalledWith({
        text: document.documentElement.style.getPropertyValue('--text'),
      }),
    )
    expect(screen.getByLabelText('Window title bar')).toBeTruthy()
    expect(document.documentElement.dataset.windowChrome).toBe('themed')
    view.rerender(<WindowChrome fullscreen />)
    expect(screen.queryByLabelText('Window title bar')).toBeNull()
    expect(document.documentElement.dataset.windowChrome).toBe('none')
    view.rerender(<WindowChrome fullscreen={false} />)
    expect(screen.getByLabelText('Window title bar')).toBeTruthy()
  },
)
it('updates buttons when an Avalon palette changes from dark to light', async () => {
  render(<WindowChrome fullscreen={false} />)
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.settings = { avalon: { palette: 'rose-pine-dawn' } }
  await act(async () => applyThemeProfile(profile))
  await waitFor(() => expect(window.winnow.windowChrome).toHaveBeenLastCalledWith({ text: '#464261' }))
})

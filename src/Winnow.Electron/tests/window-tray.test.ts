import { describe, expect, it, vi } from 'vitest'
import { WindowTrayController } from '../src/main/window-tray'

function fixture(background = false, unavailable = false) {
  const icons: Array<{ destroy: ReturnType<typeof vi.fn> }> = []
  const window = {
    show: vi.fn(),
    hide: vi.fn(),
    focus: vi.fn(),
    restore: vi.fn(),
    isMinimized: vi.fn(() => false),
    setSkipTaskbar: vi.fn(),
    setFullScreen: vi.fn(),
  }
  const createIcon = vi.fn(() => {
    if (unavailable) throw new Error('Notification area unavailable')
    const icon = { destroy: vi.fn() }
    icons.push(icon)
    return icon
  })
  const controller = new WindowTrayController({ background, window: () => window, createIcon })
  window.show.mockImplementation(() => controller.shown())
  return { controller, window, icons, createIcon }
}

describe('native window recovery and tray lifetime', () => {
  it('ordinary startup and fullscreen preference alone need no icon', () => {
    const { controller, window, createIcon } = fixture()
    controller.prepare()
    controller.preferences({ StartInFullscreen: 'True' })
    controller.ready()
    expect(window.show).toHaveBeenCalledOnce()
    expect(window.setFullScreen).toHaveBeenCalledWith(true)
    expect(createIcon).not.toHaveBeenCalled()
    expect(controller.closing()).toBe(false)
    controller.minimized()
    expect(window.hide).not.toHaveBeenCalled()
  })

  it.each(['true', 'True', ' TRUE '])('accepts saved %j tray values from either frontend', (value) => {
    const { controller, window, icons } = fixture()
    controller.preferences({ MinimizeToTray: value, CloseToTray: value })
    controller.minimized()
    expect(window.hide).toHaveBeenCalledOnce()
    expect(window.setSkipTaskbar).toHaveBeenLastCalledWith(true)
    expect(controller.closing()).toBe(true)
    expect(icons).toHaveLength(1)
  })

  it.each([null, '', 'false', 'False', '1', 'yes'])('does not hide for disabled or invalid %j', (value) => {
    const { controller, window, createIcon } = fixture()
    controller.preferences({ MinimizeToTray: value, CloseToTray: value, StartInFullscreen: value })
    controller.minimized()
    expect(controller.closing()).toBe(false)
    expect(window.hide).not.toHaveBeenCalled()
    expect(window.setFullScreen).not.toHaveBeenCalled()
    expect(createIcon).not.toHaveBeenCalled()
  })

  it('keeps one icon until the last enabled preference is disabled', () => {
    const { controller, icons, createIcon } = fixture()
    controller.preferences({ MinimizeToTray: 'true' })
    controller.preferences({ MinimizeToTray: 'true', CloseToTray: 'true' })
    controller.preferences({ CloseToTray: 'true' })
    expect(createIcon).toHaveBeenCalledOnce()
    expect(icons[0].destroy).not.toHaveBeenCalled()
    controller.preferences({})
    expect(icons[0].destroy).toHaveBeenCalledOnce()
  })

  it.each(['minimize', 'close'] as const)(
    'keeps a recovery route after disabling preferences while hidden by %s',
    (action) => {
      const { controller, window, icons } = fixture()
      controller.preferences({ MinimizeToTray: 'true', CloseToTray: 'true' })
      if (action === 'minimize') controller.minimized()
      else expect(controller.closing()).toBe(true)
      controller.preferences({})
      expect(icons[0].destroy).not.toHaveBeenCalled()
      window.isMinimized.mockReturnValue(action === 'minimize')
      controller.restore()
      expect(window.show).toHaveBeenCalledOnce()
      expect(window.setSkipTaskbar).toHaveBeenLastCalledWith(false)
      expect(window.restore).toHaveBeenCalledTimes(action === 'minimize' ? 1 : 0)
      expect(window.focus).toHaveBeenCalledOnce()
      expect(icons[0].destroy).toHaveBeenCalledOnce()
    },
  )

  it.each([false, true])(
    'background launch retains a temporary icon, including early restore %s',
    (early) => {
      const { controller, window, icons } = fixture(true)
      controller.prepare()
      controller.ready()
      expect(window.show).not.toHaveBeenCalled()
      expect(window.setSkipTaskbar).toHaveBeenLastCalledWith(true)
      if (early) controller.restore()
      controller.preferences({ StartInFullscreen: 'True' })
      if (!early) controller.restore()
      expect(window.show).toHaveBeenCalledOnce()
      expect(window.setFullScreen).not.toHaveBeenCalled()
      expect(icons[0].destroy).toHaveBeenCalledOnce()
    },
  )

  it('does not reapply fullscreen startup during preference refresh or restoration', () => {
    const { controller, window } = fixture()
    controller.preferences({ StartInFullscreen: 'True' })
    window.setFullScreen.mockClear()
    controller.restore()
    controller.preferences({ StartInFullscreen: 'False' })
    controller.preferences({ StartInFullscreen: 'True' })
    expect(window.setFullScreen).not.toHaveBeenCalled()
    expect(window.restore).not.toHaveBeenCalled()
  })

  it('leaves background launches visible and ordinary close available if tray creation fails', () => {
    const { controller, window } = fixture(true, true)
    controller.prepare()
    controller.preferences({ MinimizeToTray: 'True', CloseToTray: 'True' })
    controller.ready()
    controller.minimized()
    expect(window.show).toHaveBeenCalledOnce()
    expect(window.hide).not.toHaveBeenCalled()
    expect(controller.closing()).toBe(false)
    expect(window.setSkipTaskbar).not.toHaveBeenCalledWith(true)
  })

  it('disposes once and cannot recreate an icon or intercept close during asynchronous quit', () => {
    const { controller, window, icons, createIcon } = fixture(true)
    controller.prepare()
    controller.preferences({ MinimizeToTray: 'True', CloseToTray: 'True' })
    controller.dispose()
    controller.preferences({ MinimizeToTray: 'True' })
    controller.shown()
    controller.minimized()
    controller.restore()
    controller.ready()
    expect(controller.closing()).toBe(false)
    controller.dispose()
    expect(createIcon).toHaveBeenCalledOnce()
    expect(icons[0].destroy).toHaveBeenCalledOnce()
    expect(window.show).not.toHaveBeenCalled()
    expect(window.hide).not.toHaveBeenCalled()
  })
})

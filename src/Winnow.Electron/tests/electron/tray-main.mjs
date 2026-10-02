import { app, BrowserWindow, Notification, Tray } from 'electron'
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { close as closeInspector } from 'node:inspector'

app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
let holdPreferences = false
const preferenceWaiters = []
const fetch = globalThis.fetch
globalThis.fetch = async (input, options) => {
  const response = await fetch(input, options)
  const url = new URL(input instanceof Request ? input.url : String(input))
  if (
    holdPreferences &&
    url.pathname.endsWith('/preferences/presentation') &&
    (!options?.method || options.method === 'GET')
  )
    await new Promise((done) => preferenceWaiters.push(done))
  return response
}
let notification
let detachOnQuit = false
Notification.isSupported = () => true
Notification.prototype.show = function () {
  notification = this
  this.emit('show')
}
const icons = [],
  menus = new WeakMap(),
  taskbar = new WeakMap()
const tooltip = Tray.prototype.setToolTip
Tray.prototype.setToolTip = function (...args) {
  icons.push(this)
  if (process.env.WINNOW_FIXTURE_TRAY_FAILURE === '1') throw Error('Controlled unavailable notification area')
  return tooltip.apply(this, args)
}
const contextMenu = Tray.prototype.setContextMenu
Tray.prototype.setContextMenu = function (menu) {
  menus.set(this, menu)
  return contextMenu.call(this, menu)
}
const skipTaskbar = BrowserWindow.prototype.setSkipTaskbar
BrowserWindow.prototype.setSkipTaskbar = function (skip) {
  taskbar.set(this, skip)
  return skipTaskbar.call(this, skip)
}
const active = () => icons.filter((icon) => !icon.isDestroyed())
const snapshot = () => {
  const window = BrowserWindow.getAllWindows()[0]
  return {
    created: icons.length,
    alive: active().length,
    destroyed: icons.filter((icon) => icon.isDestroyed()).length,
    menus: active().map((icon) =>
      menus.get(icon)?.items.map((item) => (item.type === 'separator' ? 'separator' : item.label)),
    ),
    window: window
      ? {
          visible: window.isVisible(),
          minimized: window.isMinimized(),
          maximized: window.isMaximized(),
          fullscreen: window.isFullScreen(),
          skipTaskbar: taskbar.get(window) ?? false,
        }
      : null,
  }
}
globalThis.__tray = {
  snapshot,
  prepareExit() {
    detachOnQuit = true
  },
  holdPreferences() {
    holdPreferences = true
  },
  pendingPreferences() {
    return preferenceWaiters.length
  },
  releasePreferences() {
    holdPreferences = false
    preferenceWaiters.splice(0).forEach((done) => done())
  },
  activateNotification() {
    if (!notification) throw Error('No session notification was submitted')
    notification.emit('click')
  },
  open(method = 'menu') {
    const icon = active()[0]
    if (!icon) throw Error('No native tray icon exists')
    if (method === 'double-click') icon.emit('double-click')
    else
      menus
        .get(icon)
        .items.find((item) => item.label === 'Open Winnow')
        .click()
  },
  quit() {
    const icon = active()[0]
    if (!icon) throw Error('No native tray icon exists')
    menus
      .get(icon)
      .items.find((item) => item.label === 'Exit')
      .click()
  },
}
app.on('will-quit', () => {
  const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
  writeFileSync(join(directory, 'tray-exit.json'), JSON.stringify(snapshot()))
})
app.on('quit', () => {
  if (detachOnQuit) closeInspector()
})
await import('../../out/main/index.js')

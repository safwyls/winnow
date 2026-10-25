import { BrowserWindow } from 'electron'

// The observation fixture starts with a real unshown window, then uses its original show method.
const originalShow = BrowserWindow.prototype.show
const hidden = process.argv.includes('--recommendation-state-hidden')
const observation = (globalThis.__recommendationState = { allowShow: !hidden })
BrowserWindow.prototype.show = function () {
  if (observation.allowShow) originalShow.call(this)
}
await import('./identity-projections-main.mjs')

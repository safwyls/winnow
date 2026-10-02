import { Notification } from 'electron'

const state = (globalThis.__journalActivityNative = { notifications: [] })
Notification.isSupported = () => true
Notification.prototype.show = function () {
  state.notifications.push({ title: this.title, body: this.body })
  this.emit('failed', new Error('The isolated fixture blocks OS notification delivery.'))
}

// Journal and activity data use the real authenticated fixture backend.
await import('./identity-projections-main.mjs')

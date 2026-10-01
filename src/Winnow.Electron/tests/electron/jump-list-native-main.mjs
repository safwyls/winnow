import { app, BrowserWindow, nativeImage } from 'electron'
import { readFile, readdir } from 'node:fs/promises'
import { basename, join, resolve } from 'node:path'
import { pathToFileURL, fileURLToPath } from 'node:url'

const argument = process.argv.indexOf('--data-dir')
const directory = resolve(process.argv[argument + 1] ?? '')
if (argument < 0 || !basename(directory).startsWith('winnow-electron-jump-list-native-'))
  throw Error('Jump List verification requires an isolated data directory.')
app.setPath('userData', join(directory, 'electron-userdata'))
const helpers = await import(pathToFileURL(process.env.WINNOW_JUMP_LIST_MODULE).href)
const image = (width, height, pixel) => {
  const bitmap = Buffer.alloc(width * height * 4)
  for (let y = 0; y < height; y++)
    for (let x = 0; x < width; x++) bitmap.set(pixel(x, y), (y * width + x) * 4)
  return nativeImage.createFromBitmap(bitmap, { width, height })
}

void app.whenReady().then(async () => {
  const identity = helpers.jumpListAppId(directory, process.env.LOCALAPPDATA, app.getPath('userData'))
  app.setAppUserModelId(identity)
  const window = new BrowserWindow({ show: false, webPreferences: { sandbox: true } })
  await window.loadURL('about:blank')
  globalThis.__jumpListNative = {
    directory,
    identity,
    async encodeSource() {
      const source = image(80, 160, (_x, y) => (y >= 40 && y < 120 ? [0, 255, 0, 255] : [0, 0, 255, 255]))
      const bytes = helpers.encodeJumpListIcon(source)
      return {
        reserved: bytes.readUInt16LE(0),
        type: bytes.readUInt16LE(2),
        count: bytes.readUInt16LE(4),
        frames: helpers.jumpListIconSizes.map((size, index) => {
          const entry = 6 + index * 16,
            length = bytes.readUInt32LE(entry + 8),
            offset = bytes.readUInt32LE(entry + 12)
          const png = bytes.subarray(offset, offset + length)
          const decoded = nativeImage.createFromBuffer(png)
          const bitmap = decoded.toBitmap(),
            center = (Math.floor(size / 2) * size + Math.floor(size / 2)) * 4
          return {
            width: bytes[entry],
            height: bytes[entry + 1],
            planes: bytes.readUInt16LE(entry + 4),
            bits: bytes.readUInt16LE(entry + 6),
            length,
            offset,
            pngSignature: [...png.subarray(0, 8)],
            decoded: decoded.getSize(),
            centerBGRA: [...bitmap.subarray(center, center + 4)],
          }
        }),
      }
    },
    async cacheSource() {
      let source = image(64, 96, () => [255, 0, 0, 255]).toDataURL()
      const icons = new helpers.JumpListIcons(
        directory,
        async (key) => (key.provider === 'test' && key.id === 'game' ? source : null),
        nativeImage.createFromDataURL,
      )
      const first = await icons.get({ provider: 'test', id: 'game' })
      const second = await icons.get({ provider: 'test', id: 'game' })
      const files = await readdir(join(directory, 'jump-list-icons'))
      source = image(64, 96, () => [0, 0, 255, 255]).toDataURL()
      const changed = await icons.get({ provider: 'test', id: 'game' })
      const changedAgain = await icons.get({ provider: 'test', id: 'game' })
      return {
        first,
        second,
        changed,
        changedAgain,
        missingKey: await icons.get(null),
        missingArt: await icons.get({ provider: 'missing', id: 'game' }),
        files,
        filesAfterChange: await readdir(join(directory, 'jump-list-icons')),
        bytes: (await readFile(first)).toString('base64'),
      }
    },
    async publishSource() {
      const icons = new helpers.JumpListIcons(
        directory,
        async () => image(32, 32, () => [0, 255, 0, 255]).toDataURL(),
        nativeImage.createFromDataURL,
      )
      const iconPath = await icons.get({ provider: 'test', id: 'smoke' })
      const item = (title, action) => ({
        type: 'task',
        title,
        program: process.execPath,
        args: helpers.jumpListArguments(directory, action, fileURLToPath(import.meta.url)),
        iconPath,
        iconIndex: 0,
      })
      const recent = item('Jump List smoke game', '--jump-list-game 42')
      const fullscreen = item('Switch to Fullscreen Mode', '--jump-list-fullscreen')
      let published, empty, cleared
      try {
        published = app.setJumpList([
          { type: 'custom', name: 'Recently Played', items: [recent] },
          { type: 'tasks', items: [fullscreen] },
        ])
        empty = app.setJumpList([{ type: 'tasks', items: [fullscreen] }])
      } finally {
        cleared = app.setJumpList(null)
      }
      return { identity, directory, recent, fullscreen, published, empty, cleared }
    },
  }
})

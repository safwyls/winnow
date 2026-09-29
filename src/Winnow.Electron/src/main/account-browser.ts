import { BrowserWindow, WebContentsView, type Session } from 'electron'
import { AccountInputGate } from './account-input'
import { accountComposerKeys, accountInputDocument } from './account-composer'
import {
  BrowserController,
  browserControllerKey,
  browserPadScript,
  readBrowserPad,
} from './browser-controller'

export type AccountBrowser = Pick<
  BrowserWindow,
  'webContents' | 'loadURL' | 'isDestroyed' | 'on' | 'once' | 'off' | 'setTitle' | 'close' | 'destroy'
> & { setInputEnabled?(enabled: boolean): void }

/** Provider pages and the local masked composer never share a document or application preload. */
export function createAccountBrowser(parent: BrowserWindow, profile: Session, title: string): AccountBrowser {
  const fullscreen = parent.isFullScreen?.() === true
  const preferences = {
    session: profile,
    sandbox: true,
    contextIsolation: true,
    nodeIntegration: false,
    nodeIntegrationInWorker: false,
    webviewTag: false,
    webSecurity: true,
    disableDialogs: true,
    navigateOnDragDrop: false,
  }
  const window = new BrowserWindow({
    parent,
    width: 1100,
    height: 860,
    title,
    fullscreen,
    autoHideMenuBar: true,
    webPreferences: preferences,
  })
  if (!fullscreen) {
    let enabled = true
    window.webContents.on('before-input-event', (event, input) => {
      if (input.type === 'keyDown' && input.key === 'Escape') {
        event.preventDefault()
        window.close()
      } else if (!enabled) event.preventDefault()
    })
    window.webContents.on('before-mouse-event', (event) => {
      if (!enabled) event.preventDefault()
    })
    return Object.assign(window, {
      setInputEnabled(value: boolean) {
        enabled = value
      },
    })
  }
  const provider = new WebContentsView({ webPreferences: preferences })
  window.contentView.addChildView(provider)
  const contents = provider.webContents,
    controls = window.webContents,
    filter = new BrowserController()
  let disposed = false,
    enabled = true,
    composing = false,
    upper = false,
    symbols = false,
    selected = 0,
    generation = 0,
    busy = false,
    poll: ReturnType<typeof setTimeout> | undefined
  let actions = Promise.resolve()
  const toolbarReady = window
    .loadURL('about:blank')
    .then(() =>
      controls.executeJavaScript(
        `document.open();document.write(${JSON.stringify(accountInputDocument())});document.close()`,
      ),
    )
  const gate = new AccountInputGate(contents, async () => {
    await toolbarReady
  })
  function layout() {
    const bounds = window.getContentBounds()
    provider.setBounds({ x: 0, y: 80, width: bounds.width, height: Math.max(0, bounds.height - 80) })
  }
  function status(message: string) {
    if (!disposed)
      void controls
        .executeJavaScript(`document.getElementById('problem').textContent=${JSON.stringify(message)}`)
        .catch(() => {})
  }
  function discard(message = '') {
    generation++
    composing = false
    upper = false
    symbols = false
    selected = 0
    filter.reset()
    if (disposed) return
    provider.setVisible(true)
    void controls
      .executeJavaScript(
        `document.open();document.write(${JSON.stringify(accountInputDocument())});document.close()`,
      )
      .then(() => {
        if (!disposed) {
          contents.focus()
          status(message)
        }
      })
      .catch(() => {})
  }
  async function compose() {
    if (disposed || !enabled || composing || contents.isLoading()) return
    generation++
    composing = true
    upper = false
    symbols = false
    selected = 0
    filter.reset()
    provider.setVisible(false)
    await controls.executeJavaScript(
      `document.open();document.write(${JSON.stringify(accountInputDocument(true))});document.close();(()=>{const input=document.getElementById('draft');input.dataset.start='0';input.dataset.end='0';const save=()=>{input.dataset.start=String(input.selectionStart??input.value.length);input.dataset.end=String(input.selectionEnd??input.value.length)};input.addEventListener('input',save);input.addEventListener('select',()=>{if(document.activeElement===input)save()});input.focus()})()`,
    )
    if (composing && !disposed) controls.focus()
  }
  async function done() {
    if (!composing || !enabled || disposed) return
    const before = generation
    const text: unknown = await controls.executeJavaScript("document.getElementById('draft')?.value??''")
    if (!composing || !enabled || disposed || generation !== before) return
    discard()
    if (typeof text === 'string') await gate.text(text)
  }
  async function changeKeys() {
    const keys = accountComposerKeys(upper, symbols)
    selected = Math.min(selected, keys.length - 1)
    // Keep the password field and its value in this local document; only redraw the key labels.
    await controls.executeJavaScript(
      `(()=>{const keys=${JSON.stringify(keys)},nav=document.querySelector('nav');nav.replaceChildren(...keys.map((key,index)=>{const a=document.createElement('a');a.id='key-'+index;a.href='#key-'+index;a.textContent=key;return a}));document.getElementById('key-'+${selected})?.setAttribute('data-selected','true')})()`,
    )
  }
  async function action(index: number) {
    if (!composing || !enabled || disposed) return
    const key = accountComposerKeys(upper, symbols)[index]
    if (!key) return
    if (key === 'Cancel') {
      discard()
      return
    }
    if (key === 'Done') {
      await done()
      return
    }
    if (key === 'Shift') {
      upper = !upper
      await changeKeys()
      return
    }
    if (key === 'Symbols') {
      symbols = !symbols
      await changeKeys()
      return
    }
    await controls.executeJavaScript(
      `(()=>{const input=document.getElementById('draft');if(!input)return;let start=Number(input.dataset.start??input.value.length),end=Number(input.dataset.end??start);const value=${JSON.stringify(key === 'Space' ? ' ' : key)};if(value==='Backspace'){if(start===end&&start>0)start-=Array.from(input.value.slice(0,start)).at(-1).length;input.setRangeText('',start,end,'end');end=start}else if(input.value.length-end+start+value.length<=4096){input.setRangeText(value,start,end,'end');end=start+value.length}input.dataset.start=String(end);input.dataset.end=String(end)})()`,
    )
  }
  async function controller(button: number) {
    if (composing) {
      if (button === 1) {
        discard()
        return
      }
      if (button === 9) {
        await done()
        return
      }
      if (button === 0) {
        await action(selected)
        return
      }
      if (button === 2) {
        await action(accountComposerKeys(upper, symbols).indexOf('Backspace'))
        return
      }
      if (button === 3) {
        await action(accountComposerKeys(upper, symbols).indexOf('Shift'))
        return
      }
      const delta = ({ 12: -10, 13: 10, 14: -1, 15: 1 } as Record<number, number>)[button]
      if (delta) {
        selected = Math.max(0, Math.min(accountComposerKeys(upper, symbols).length - 1, selected + delta))
        await controls.executeJavaScript(
          `document.querySelector('[data-selected=true]')?.removeAttribute('data-selected');document.getElementById('key-'+${selected})?.setAttribute('data-selected','true')`,
        )
      }
      return
    }
    if (button === 1) {
      window.close()
      return
    }
    if (!enabled) return
    if (button === 3) {
      await compose()
      return
    }
    const key = browserControllerKey(button, false)
    if (key) await gate.key(key)
  }
  controls.setWindowOpenHandler(() => ({ action: 'deny' }))
  controls.on('will-navigate', (event) => event.preventDefault())
  controls.on('will-redirect', (event) => event.preventDefault())
  controls.on('will-frame-navigate', (event) => event.preventDefault())
  controls.on('did-navigate-in-page', (_event, url, main) => {
    if (!main || !composing) return
    const match = /^about:blank#key-(\d{1,2})$/.exec(url)
    if (!match) return
    const before = generation
    void controls.executeJavaScript("history.replaceState(null,'','about:blank')").catch(() => {})
    actions = actions.then(async () => {
      if (disposed || !composing || before !== generation) return
      busy = true
      try {
        await action(Number(match[1]))
      } catch {
        status('Could not edit this draft. Try again.')
      } finally {
        busy = false
      }
    })
  })
  for (const target of [controls, contents])
    target.on('before-input-event', (event, input) => {
      if (input.type === 'keyDown' && input.key === 'Escape') {
        event.preventDefault()
        if (composing) discard()
        else window.close()
      }
    })
  contents.on('before-input-event', (event) => {
    if (!enabled) event.preventDefault()
  })
  contents.on('before-mouse-event', (event) => {
    if (!enabled) event.preventDefault()
  })
  contents.on('did-start-navigation', (_event, _url, _inPlace, main) => {
    if (main !== false) {
      gate.navigated()
      filter.reset()
      if (composing) discard('The page changed. Choose a field before entering text.')
    }
  })
  window.on('resize', layout)
  window.on('blur', () => filter.reset())
  window.once('closed', () => {
    disposed = true
    enabled = false
    gate.setEnabled(false)
    generation++
    filter.reset()
    if (poll) clearTimeout(poll)
    if (!contents.isDestroyed()) contents.close()
  })
  async function tick() {
    try {
      if (disposed) return
      if (!window.isFocused() || parent.isDestroyed() || busy) {
        filter.reset()
        return
      }
      const before = generation,
        sample = await parent.webContents.executeJavaScriptInIsolatedWorld(321, [{ code: browserPadScript }])
      if (disposed || before !== generation || !window.isFocused()) {
        filter.reset()
        return
      }
      for (const button of filter.sample(readBrowserPad(sample), true, Date.now())) {
        busy = true
        try {
          await controller(button)
        } finally {
          busy = false
        }
        if (disposed) break
      }
    } catch {
      filter.reset()
      status('Browser input is unavailable. Press Escape to close.')
    } finally {
      if (!disposed) poll = setTimeout(() => void tick(), 50)
    }
  }
  layout()
  contents.setZoomFactor(1.5)
  void tick()
  return {
    webContents: contents,
    loadURL: async (url, options) => {
      await toolbarReady
      if (!disposed) {
        await contents.loadURL(url, options)
        contents.focus()
      }
    },
    isDestroyed: () => window.isDestroyed(),
    on: window.on.bind(window),
    once: window.once.bind(window),
    off: window.off.bind(window),
    setTitle: window.setTitle.bind(window),
    close: window.close.bind(window),
    destroy: window.destroy.bind(window),
    setInputEnabled(value) {
      enabled = value
      gate.setEnabled(value)
      filter.reset()
      if (!value) {
        if (composing) discard('Reading account pages… B cancels.')
        else status('Reading account pages… B cancels.')
      }
    },
  }
}

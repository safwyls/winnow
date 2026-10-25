import { contextBridge, ipcRenderer } from 'electron'

// This preload belongs only to the Epic provider view. It exposes no application API.
// Sandboxed Electron does not expose process.isMainFrame; main independently verifies senderFrame.
if (window.top === window.self) {
  const config: { origins: string[]; documentToken: string; bridge: boolean } | null =
    ipcRenderer.sendSync('winnow:epic:document')
  const origin = `${location.protocol}//${location.hostname.toLowerCase()}:${location.port || (location.protocol === 'https:' ? '443' : '80')}`
  if (config?.bridge && location.protocol === 'https:' && config.origins.includes(origin)) {
    const post = (kind: 'exchange' | 'signed-in', value?: unknown) => {
      if (
        location.protocol !== 'https:' ||
        `${location.protocol}//${location.hostname.toLowerCase()}:${location.port || '443'}` !== origin
      )
        return
      if (kind === 'exchange' && (typeof value !== 'string' || value.length > 4096)) return
      ipcRenderer.send('winnow:epic:capture', { documentToken: config.documentToken, kind, value })
    }
    contextBridge.exposeInMainWorld('ue', {
      signinprompt: {
        requestexchangecodesignin: (code: unknown) => post('exchange', code),
        registersignincompletecallback: () => post('signed-in'),
      },
      common: { launchexternalurl: (_url: unknown) => {} },
    })
  }
}

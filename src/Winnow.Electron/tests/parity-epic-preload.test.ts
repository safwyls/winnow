import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { transformSync } from 'esbuild'
import { describe, expect, it, vi } from 'vitest'
const source = transformSync(readFileSync(new URL('../src/preload/epic.ts', import.meta.url), 'utf8'), {
  loader: 'ts',
  format: 'cjs',
}).code
function load(
  origin = 'https://www.epicgames.com',
  frame = false,
  bridge = true,
  origins = ['https://www.epicgames.com:443'],
) {
  const send = vi.fn(),
    bootstrap = vi.fn(() => ({ origins, documentToken: 'fixture-document', bridge })),
    exposed: Record<string, any> = {}
  const window = { top: {}, self: {} }
  if (!frame) window.top = window.self
  const location = new URL(origin)
  runInNewContext(source, {
    window,
    location,
    process: { sandboxed: true },
    require: () => ({
      ipcRenderer: { sendSync: bootstrap, send },
      contextBridge: {
        exposeInMainWorld: (key: string, value: unknown) => {
          exposed[key] = value
        },
      },
    }),
  })
  return { send, bootstrap, exposed, location }
}
describe('Epic capture-only sandbox preload', () => {
  it('arms in a sandbox without process.isMainFrame and exposes only the narrow launcher facade', () => {
    const h = load()
    expect(h.bootstrap).toHaveBeenCalledWith('winnow:epic:document')
    expect(Object.keys(h.exposed)).toEqual(['ue'])
    h.exposed.ue.signinprompt.requestexchangecodesignin('fixture-code')
    expect(h.send).toHaveBeenCalledWith('winnow:epic:capture', {
      documentToken: 'fixture-document',
      kind: 'exchange',
      value: 'fixture-code',
    })
    h.exposed.ue.signinprompt.registersignincompletecallback()
    expect(h.send).toHaveBeenCalledWith('winnow:epic:capture', {
      documentToken: 'fixture-document',
      kind: 'signed-in',
      value: undefined,
    })
    h.exposed.ue.common.launchexternalurl('steam://run/1')
    expect(h.send).toHaveBeenCalledTimes(2)
  })
  it.each([
    'https://accounts.google.com',
    'https://www.epicgames.com.evil.test',
    'http://www.epicgames.com',
    'https://www.epicgames.com:8443',
  ])('defines no launcher facade for %s', (origin) => {
    expect(load(origin).exposed).toEqual({})
  })
  it('never bootstraps inside a frame even on a trusted origin', () => {
    const h = load('https://www.epicgames.com', true)
    expect(h.bootstrap).not.toHaveBeenCalled()
    expect(h.exposed).toEqual({})
  })
  it('cannot arm with an empty trusted set or a disabled bridge strategy', () => {
    expect(load('https://www.epicgames.com', false, true, []).exposed).toEqual({})
    expect(load('https://www.epicgames.com', false, false).exposed).toEqual({})
  })
  it('bounds callback values and refuses a callback after origin changes', () => {
    const h = load()
    h.exposed.ue.signinprompt.requestexchangecodesignin({ code: 'not-a-string' })
    h.exposed.ue.signinprompt.requestexchangecodesignin('x'.repeat(4097))
    h.location.hostname = 'accounts.google.com'
    h.exposed.ue.signinprompt.requestexchangecodesignin('stale')
    expect(h.send).not.toHaveBeenCalled()
  })
})

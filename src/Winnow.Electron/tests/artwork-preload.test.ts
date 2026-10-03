import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { transformSync } from 'esbuild'
import { expect, it, vi } from 'vitest'
import type { WinnowBridge } from '../src/shared/bridge'

const source = transformSync(readFileSync(new URL('../src/preload/index.ts', import.meta.url), 'utf8'), {
  loader: 'ts',
  format: 'cjs',
}).code

it.each([new Uint8Array([137, 80, 78, 71]), null])(
  'forwards binary artwork through its named channel with cancellation identity',
  async (bytes) => {
    const invoke = vi.fn(async () => structuredClone(bytes))
    let bridge!: WinnowBridge
    runInNewContext(source, {
      require: () => ({
        ipcRenderer: { invoke },
        contextBridge: {
          exposeInMainWorld: (_key: string, value: WinnowBridge) => {
            bridge = value
          },
        },
      }),
    })
    const requestId = 'a'.repeat(32)
    const result = await bridge.artworkBytes!('igdb-shot', 'fixture', 1280, requestId)
    expect(result).toEqual(bytes)
    if (bytes) expect(result).toBeInstanceOf(Uint8Array)
    expect(invoke).toHaveBeenCalledExactlyOnceWith(
      'winnow:artwork:bytes',
      'igdb-shot',
      'fixture',
      1280,
      requestId,
    )
    expect(Object.isFrozen(bridge)).toBe(true)
  },
)

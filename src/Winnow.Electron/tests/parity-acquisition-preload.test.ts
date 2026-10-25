import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { transformSync } from 'esbuild'
import { expect, it, vi } from 'vitest'
import type { WinnowBridge } from '../src/shared/bridge'

const source = transformSync(readFileSync(new URL('../src/preload/index.ts', import.meta.url), 'utf8'), {
  loader: 'ts',
  format: 'cjs',
}).code

it.each([
  { saved: true, ownershipCount: 0 },
  { saved: true, ownershipCount: 3 },
  { saved: false, ownershipCount: 3 },
])('forwards the acquisition destination result and source count $saved/$ownershipCount', async (result) => {
  const invoke = vi.fn().mockResolvedValue(result)
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
  expect(await bridge.exportAcquisitions!()).toEqual(result)
  expect(invoke).toHaveBeenCalledExactlyOnceWith('winnow:acquisitions:export')
})

import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { transformSync } from 'esbuild'
import { expect, it, vi } from 'vitest'
import { DEFAULT_TYPOGRAPHY } from '../src/shared/typography'
import type { WinnowBridge } from '../src/shared/bridge'

it('exposes only the named typography invocation from the frozen preload bridge', async () => {
  const source = transformSync(readFileSync(new URL('../src/preload/index.ts', import.meta.url), 'utf8'), {
    loader: 'ts',
    format: 'cjs',
  }).code
  const invoke = vi.fn(async () => {})
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
  await bridge.setPopoutTypography!(DEFAULT_TYPOGRAPHY)
  expect(invoke).toHaveBeenCalledExactlyOnceWith('winnow:typography:popouts', DEFAULT_TYPOGRAPHY)
  expect(Object.isFrozen(bridge)).toBe(true)
})

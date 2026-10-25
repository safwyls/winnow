import { describe, expect, it } from 'vitest'
import { isAbsolute } from 'node:path'
import { BackendTransport, readDiscovery } from '../src/main/transport'

// Opt-in only: this test attaches read-only to an already running, explicitly selected test host.
const directory = process.env.WINNOW_ELECTRON_TEST_DATA_DIR
describe.skipIf(!directory)('live backend transport', () => {
  it('receives the registration marker before returning authenticated snapshots', async () => {
    expect(isAbsolute(directory!)).toBe(true)
    const observed: string[] = []
    const transport = new BackendTransport({
      discover: () => readDiscovery(directory!),
      onEvent: (event) => observed.push(event.kind),
    })
    try {
      const library = await transport.request<{ games: unknown[] }>({ route: 'library.get' })
      expect(observed[0]).toBe('resync-required')
      expect(library.ok).toBe(true)
      expect(Array.isArray(library.data?.games)).toBe(true)
      expect(transport.connection().connected).toBe(true)
      const health = await transport.request<{ apiVersion: string }>({ route: 'health.get' })
      expect(health.data?.apiVersion).toBe('1')
    } finally {
      transport.stop()
    }
  }, 20000)
})

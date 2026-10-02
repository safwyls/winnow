import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { isAbsolute } from 'node:path'
import { BackendTransport, readDiscovery } from '../src/main/transport'
import type { ApiRequest } from '../src/shared/bridge'
import type { ArtworkState, ManualGame } from '../src/renderer/api/types'

const directory = process.env.WINNOW_TEST_DATA_DIR
describe.skipIf(!directory)('artwork file import through the production local API', () => {
  let transport: BackendTransport
  beforeAll(async () => {
    if (!directory || !isAbsolute(directory) || !/[\\/]winnow-electron-[^\\/]*$/i.test(directory))
      throw Error('Use an explicit throwaway winnow-electron-* data directory.')
    await readDiscovery(directory)
    transport = new BackendTransport({ discover: () => readDiscovery(directory) })
  })
  afterAll(() => transport?.stop())
  async function api<T>(request: ApiRequest): Promise<T> {
    const result = await transport.request<T>(request)
    expect(result.ok, `${request.route}: ${'message' in result ? result.message : ''}`).toBe(true)
    if (!result.ok) throw Error(result.message)
    return result.data as T
  }
  const slots = ['Hero', 'Cover', 'Icon'] as const
  // One real BMP pixel verifies decoding without a network provider or opaque copied fixture.
  function image() {
    const bytes = Buffer.alloc(58)
    bytes.write('BM')
    bytes.writeUInt32LE(58, 2)
    bytes.writeUInt32LE(54, 10)
    bytes.writeUInt32LE(40, 14)
    bytes.writeInt32LE(1, 18)
    bytes.writeInt32LE(1, 22)
    bytes.writeUInt16LE(1, 26)
    bytes.writeUInt16LE(24, 28)
    bytes.writeUInt32LE(4, 34)
    bytes[56] = 255
    return bytes
  }
  it.each(slots)(
    'decodes %s, refuses invalid bytes and stale revisions, and leaves the other slots unchanged',
    async (slot) => {
      const game = await api<ManualGame>({
        route: 'manual.create',
        body: {
          title: `Artwork import ${slot} ${crypto.randomUUID()}`,
          firstReleaseYear: 2025,
          platformLabel: 'Test platform',
        },
      })
      const params = { workId: game.workId, slot }
      try {
        const states = await Promise.all(
          slots.map((name) => api<ArtworkState>({ route: 'artwork.get', params: { ...params, slot: name } })),
        )
        const before = states[slots.indexOf(slot)]!
        const input = { ...params, revision: before.revision }
        const invalid = await transport.importArtwork(input, new Uint8Array([1, 2, 3]))
        expect(invalid).toMatchObject({ ok: true, data: { success: false } })
        expect(await api({ route: 'artwork.get', params })).toEqual(before)

        expect(await transport.importArtwork(input, image())).toMatchObject({
          ok: true,
          data: { success: true },
        })
        const saved = await api<ArtworkState>({ route: 'artwork.get', params })
        expect(saved.revision).not.toBe(before.revision)
        expect(saved.current?.sourceId).toBe('user')
        expect(saved.current?.isCurrent).toBe(true)
        expect(
          await transport.artwork(saved.current!.previewKey.provider, saved.current!.previewKey.id, 64),
        ).toMatch(/^data:image\/png;base64,/)
        for (const [index, other] of slots.entries())
          if (other !== slot)
            expect(await api({ route: 'artwork.get', params: { ...params, slot: other } })).toEqual(
              states[index],
            )

        expect(await transport.importArtwork(input, image())).toMatchObject({ ok: false, status: 409 })
        expect(await api({ route: 'artwork.get', params })).toEqual(saved)
        expect(
          await api({ route: 'artwork.reset', params, body: { revision: saved.revision } }),
        ).toMatchObject({ success: true })
        expect((await api<ArtworkState>({ route: 'artwork.get', params })).current).toBeNull()
      } finally {
        await api({ route: 'manual.delete', params: { ownershipId: game.ownershipId } })
      }
    },
    30_000,
  )
})

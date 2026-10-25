import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdtemp, writeFile, rm } from 'node:fs/promises'
import { basename, dirname, join, resolve } from 'node:path'
import { tmpdir } from 'node:os'
import { artworkFileLimit, importArtworkFile, readArtworkFile } from '../src/main/artwork-import'
import { BackendTransport } from '../src/main/transport'

const input = { workId: 42, slot: 'Cover' as const, revision: 'A'.repeat(64) }
const transports: BackendTransport[] = [],
  directories: string[] = []
afterEach(async () => {
  transports.splice(0).forEach((transport) => transport.stop())
  await Promise.all(
    directories.splice(0).map((directory) => {
      const target = resolve(directory)
      if (dirname(target) !== resolve(tmpdir()) || !basename(target).startsWith('winnow-electron-artwork-'))
        throw Error('Refusing to remove a directory outside the artwork test fixture.')
      return rm(target, { recursive: true, force: true })
    }),
  )
})
function picker() {
  return {
    choose: vi.fn().mockResolvedValue('test-owned-image.png'),
    read: vi.fn().mockResolvedValue(new Uint8Array([1, 2, 3])),
    upload: vi
      .fn()
      .mockResolvedValue({ ok: true, status: 200, data: { success: true, message: 'Artwork saved.' } }),
  }
}
describe('native artwork file import boundary', () => {
  it.each([
    null,
    {},
    { ...input, workId: '../private' },
    { ...input, workId: 0 },
    { ...input, slot: 'Other' },
    { ...input, revision: 'bad' },
  ])('rejects invalid renderer targets before opening the picker: %s', async (value) => {
    const ports = picker()
    expect(await importArtworkFile(value, ports)).toMatchObject({ ok: false, status: 400 })
    expect(ports.choose).not.toHaveBeenCalled()
    expect(ports.upload).not.toHaveBeenCalled()
  })
  it('cancels without reading a file or sending an image and uses only the selected path', async () => {
    const ports = picker()
    ports.choose.mockResolvedValueOnce(null)
    expect(await importArtworkFile(input, ports)).toBeNull()
    expect(ports.read).not.toHaveBeenCalled()
    expect(ports.upload).not.toHaveBeenCalled()
    expect(await importArtworkFile({ ...input, path: 'renderer-invented.png' }, ports)).toMatchObject({
      ok: true,
    })
    expect(ports.read).toHaveBeenCalledWith('test-owned-image.png')
    expect(ports.upload).toHaveBeenCalledWith(expect.objectContaining(input), new Uint8Array([1, 2, 3]))
  })
  it.each([0, artworkFileLimit + 1])('refuses %s selected bytes before upload', async (length) => {
    const ports = picker()
    ports.read.mockResolvedValue(new Uint8Array(length))
    expect(await importArtworkFile(input, ports)).toMatchObject({ ok: false })
    expect(ports.upload).not.toHaveBeenCalled()
  })
  it('reads actual selected bytes with an independent filesystem size limit', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'winnow-electron-artwork-'))
    directories.push(directory)
    const file = join(directory, 'sample.png')
    await writeFile(file, new Uint8Array([1, 2, 3]))
    expect([...(await readArtworkFile(file))]).toEqual([1, 2, 3])
    await expect(readArtworkFile(directory)).rejects.toThrow('Choose an image file')
    await writeFile(file, new Uint8Array(artworkFileLimit + 1))
    await expect(readArtworkFile(file)).rejects.toThrow('16 MiB')
  })
})
describe('authenticated binary artwork transport', () => {
  function transport(reply: () => Promise<Response>) {
    const fetcher = vi.fn<typeof fetch>(async (url, init) => {
      if (String(url).endsWith('/events'))
        return new Response(
          new ReadableStream({
            start(controller) {
              controller.enqueue(
                new TextEncoder().encode(
                  'id: artwork:0\nevent: change\ndata: {"epoch":"artwork","sequence":0,"kind":"resync-required"}\n\n',
                ),
              )
            },
          }),
          { headers: { 'Content-Type': 'text/event-stream' } },
        )
      expect(init?.redirect).toBe('error')
      return reply()
    })
    const value = new BackendTransport({
      discover: async () => ({
        address: 'http://127.0.0.1:32123/',
        token: 'private-artwork-token',
        apiVersion: '1',
        processId: 123,
        epoch: 'artwork',
      }),
      fetch: fetcher,
    })
    transports.push(value)
    return { value, fetcher }
  }
  it('posts bounded image bytes to the selected revision and keeps credentials in main', async () => {
    const test = transport(async () => Response.json({ success: true, message: 'Artwork saved.' }))
    const result = await test.value.importArtwork(input, new Uint8Array([1, 2, 3]))
    expect(result).toEqual({ ok: true, status: 200, data: { success: true, message: 'Artwork saved.' } })
    const command = test.fetcher.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(String(command[0])).toBe(
      `http://127.0.0.1:32123/api/v1/works/42/artwork/Cover/image?revision=${input.revision}`,
    )
    expect(command[1]?.headers).toMatchObject({
      Authorization: 'Bearer private-artwork-token',
      'Content-Type': 'application/octet-stream',
    })
    expect([...new Uint8Array(command[1]?.body as ArrayBuffer)]).toEqual([1, 2, 3])
    expect(JSON.stringify(result)).not.toContain('private-artwork-token')
  })
  it('keeps a refusal and revision conflict distinct and never retries an uncertain upload', async () => {
    let stage = 0
    const test = transport(async () => {
      stage++
      if (stage === 1) return Response.json({ success: false, message: 'Choose a valid image.' })
      if (stage === 2) return Response.json({ detail: 'Changed private-artwork-token' }, { status: 409 })
      throw new Error('Disconnected')
    })
    expect(await test.value.importArtwork(input, new Uint8Array([1]))).toMatchObject({
      ok: true,
      data: { success: false },
    })
    expect(await test.value.importArtwork(input, new Uint8Array([1]))).toMatchObject({
      ok: false,
      status: 409,
      message: 'Changed [redacted]',
    })
    expect(await test.value.importArtwork(input, new Uint8Array([1]))).toMatchObject({
      ok: false,
      status: 0,
      message: expect.stringContaining('may have been saved'),
    })
    expect(stage).toBe(3)
  })
})

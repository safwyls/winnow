import { nativeImage } from 'electron'

// Test-owned cover pixels make dimming and image reuse measurable without an artwork provider.
const pixels = Buffer.alloc(600 * 900 * 4)
for (let y = 0; y < 900; y++)
  for (let x = 0; x < 600; x++) {
    const offset = (y * 600 + x) * 4
    pixels[offset] = 35 + Math.floor(x / 3)
    pixels[offset + 1] = 55 + Math.floor(y / 6)
    pixels[offset + 2] = 180 - Math.floor(x / 5)
    pixels[offset + 3] = 255
  }
const cover = nativeImage.createFromBitmap(pixels, { width: 600, height: 900 }).toPNG()
globalThis.__libraryDimmingArtwork = false
const original = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (
    globalThis.__libraryDimmingArtwork &&
    url.hostname === '127.0.0.1' &&
    (!init?.method || init.method === 'GET')
  ) {
    const work = /^\/api\/v1\/works\/(\d+)\/artwork\/Cover$/.exec(url.pathname)
    if (work)
      return Response.json({
        revision: 'fixture-cover',
        current: { previewKey: { provider: 'fixture', id: `library-${work[1]}` } },
      })
    if (url.pathname === '/api/v1/artwork/image' && url.searchParams.get('provider') === 'fixture') {
      return new Response(cover, { headers: { 'content-type': 'image/png' } })
    }
  }
  return original(input, init)
}
await import('../../out/main/index.js')

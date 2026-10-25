import { nativeImage } from 'electron'

// Known portrait pixels exercise the production image bridge and contain sizing.
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
const originalFetch = globalThis.fetch
globalThis.fetch = (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && (!init?.method || init.method === 'GET')) {
    const work = /^\/api\/v1\/works\/(\d+)\/artwork\/Cover$/.exec(url.pathname)
    if (work)
      return Promise.resolve(
        Response.json({
          revision: 'browse-portrait',
          current: { previewKey: { provider: 'fixture', id: `browse-${work[1]}` } },
        }),
      )
    if (url.pathname === '/api/v1/artwork/image' && url.searchParams.get('provider') === 'fixture')
      return Promise.resolve(new Response(cover, { headers: { 'content-type': 'image/png' } }))
  }
  return originalFetch(input, init)
}
await import('./layout-main.mjs')

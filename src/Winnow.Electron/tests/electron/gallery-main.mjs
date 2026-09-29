import { nativeImage, shell } from 'electron'

// Deterministic, test-owned images travel through the production artwork IPC and renderer.
const pixels = Buffer.alloc(1280 * 720 * 4)
for (let y = 0; y < 720; y++)
  for (let x = 0; x < 1280; x++) {
    const offset = (y * 1280 + x) * 4
    pixels[offset] = 110 + Math.floor(y / 12)
    pixels[offset + 1] = 65 + Math.floor(x / 12)
    pixels[offset + 2] = x < 8 || y < 8 || x > 1271 || y > 711 ? 235 : 20
    pixels[offset + 3] = 255
  }
const png = nativeImage.createFromBitmap(pixels, { width: 1280, height: 720 }).toPNG()
const originalFetch = globalThis.fetch
globalThis.__galleryCount = 10
globalThis.__openedInstallationFolders = []
// Record the final OS handoff without opening Explorer on the test runner.
shell.openPath = async (path) => {
  globalThis.__openedInstallationFolders.push(path)
  return ''
}
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && (!init?.method || init.method === 'GET')) {
    if (url.pathname === '/api/v1/artwork/image' && url.searchParams.get('provider') === 'igdb-shot')
      return new Response(png, { headers: { 'content-type': 'image/png' } })
    if (/^\/api\/v1\/games\/\d+\/details$/.test(url.pathname)) {
      const response = await originalFetch(input, init)
      if (!response.ok) return response
      const body = await response.json()
      return Response.json({
        ...body,
        images: [
          {
            source: 'igdb',
            kind: 'screenshot',
            imageIds: Array.from(
              { length: globalThis.__galleryCount },
              (_, index) => `fixture_${index}`,
            ).join(','),
          },
        ],
      })
    }
  }
  return originalFetch(input, init)
}
await import('../../out/main/index.js')

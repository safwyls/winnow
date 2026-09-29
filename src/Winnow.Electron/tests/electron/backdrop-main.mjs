import { nativeImage } from 'electron'

function picture(width, height, variant) {
  const pixels = Buffer.alloc(width * height * 4)
  for (let y = 0; y < height; y++)
    for (let x = 0; x < width; x++) {
      const offset = (y * width + x) * 4
      pixels[offset] = variant === 0 ? 55 + Math.floor((x / width) * 170) : 110
      pixels[offset + 1] = 80 + Math.floor((y / height) * 130)
      pixels[offset + 2] = variant === 0 ? 60 : 50 + Math.floor((x / width) * 150)
      pixels[offset + 3] = 255
    }
  return nativeImage.createFromBitmap(pixels, { width, height }).toPNG()
}
const pictures = {
  hero: picture(3840, 1240, 0),
  landscape: picture(1920, 1080, 1),
  cover: picture(600, 900, 0),
}
const fixture = (globalThis.__backdropFixture = {
  kind: 'hero',
  delay: false,
  requests: [],
  aborted: 0,
  pending: [],
})
const original = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && (!init?.method || init.method === 'GET')) {
    if (/^\/api\/v1\/works\/\d+\/backdrop$/.test(url.pathname))
      return Response.json({
        candidates:
          fixture.kind === 'missing'
            ? []
            : [
                {
                  key: {
                    provider: fixture.kind === 'hero' ? 'steam-hero' : 'igdb-backdrop',
                    id: fixture.kind,
                  },
                  aspectRatio: fixture.kind === 'hero' ? 3840 / 1240 : 16 / 9,
                  fitWholeHero: fixture.kind === 'hero',
                },
              ],
        coverKey: fixture.kind === 'missing' ? null : { provider: 'fixture', id: 'cover' },
      })
    if (url.pathname === '/api/v1/artwork/image') {
      const id = url.searchParams.get('id')
      fixture.requests.push({
        provider: url.searchParams.get('provider'),
        id,
        width: Number(url.searchParams.get('width')),
      })
      const response = () =>
        new Response(pictures[id] ?? pictures.cover, { headers: { 'content-type': 'image/png' } })
      if (fixture.delay && id === fixture.kind)
        return new Promise((resolve, reject) => {
          fixture.pending.push(() => resolve(response()))
          init.signal.addEventListener(
            'abort',
            () => {
              fixture.aborted++
              reject(init.signal.reason)
            },
            { once: true },
          )
        })
      return response()
    }
  }
  return original(input, init)
}
await import('../../out/main/index.js')

import { dialog, nativeImage, shell } from 'electron'

// Deterministic providers exercise the production main, preload and renderer without accounts.
const pixels = Buffer.alloc(800 * 450 * 4)
for (let y = 0; y < 450; y++)
  for (let x = 0; x < 800; x++) {
    const offset = (y * 800 + x) * 4
    pixels[offset] = 80 + Math.floor(y / 4)
    pixels[offset + 1] = 60 + Math.floor(x / 5)
    pixels[offset + 2] = 30
    pixels[offset + 3] = 255
  }
const png = nativeImage.createFromBitmap(pixels, { width: 800, height: 450 }).toPNG()
const fixture = (globalThis.__artworkBrowser = { writes: [], links: [], count: 24, picked: 0 })
shell.openExternal = async (url) => {
  fixture.links.push(url)
}
dialog.showOpenDialog = async () => {
  fixture.picked++
  return { canceled: true, filePaths: [] }
}
const states = new Map()
fixture.reset = () => states.clear()
const candidate = (source, slot, index) => ({
  sourceId: source,
  sourceName: source === 'steam' ? 'Steam' : 'IGDB',
  assetId: `${slot}_${index}`,
  previewKey: { provider: 'igdb-shot', id: `${slot}_${index}` },
  offerId: `${source}:${slot}:${index}`,
  pageUrl: 'https://www.steamgriddb.com/grid/1',
  creator: 'Fixture artist',
  width: 1920,
  height: 1080,
  isCurrent: false,
})
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return originalFetch(input, init)
  if (url.pathname === '/api/v1/artwork/image' && url.searchParams.get('provider') === 'igdb-shot')
    return new Response(png, { headers: { 'content-type': 'image/png' } })
  if (url.pathname === '/api/v1/artwork/sources')
    return Response.json([
      { id: 'steam', name: 'Steam', slots: [0, 1, 2] },
      { id: 'igdb', name: 'IGDB', slots: [0, 1] },
    ])
  const match = /^\/api\/v1\/works\/(\d+)\/artwork\/(Hero|Cover|Icon)(\/\w+)?$/.exec(url.pathname)
  if (match) {
    const [, workId, slot, suffix] = match,
      key = `${workId}:${slot}`
    if (!states.has(key)) states.set(key, { current: null, revision: 'A'.repeat(64) })
    const state = states.get(key)
    if (suffix === '/browse') {
      const source = url.searchParams.get('source'),
        start = url.searchParams.get('cursor') ? fixture.count : 0
      return Response.json({
        items: Array.from({ length: start ? 1 : fixture.count }, (_, index) =>
          candidate(source, slot, start + index),
        ),
        nextCursor: start ? null : 'next',
        canRetry: false,
      })
    }
    if (!init?.method || init.method === 'GET') return Response.json(state)
    const body = JSON.parse(init.body)
    if (body.revision !== state.revision)
      return Response.json({ detail: 'Artwork changed elsewhere.' }, { status: 409 })
    fixture.writes.push({ workId, slot, suffix, method: init.method, body })
    if (suffix === '/reset') state.current = null
    else {
      const [source, chosenSlot, index] = body.offerId.split(':')
      state.current = { ...candidate(source, chosenSlot, Number(index)), isCurrent: true }
    }
    state.revision =
      (Number.parseInt(state.revision.slice(0, 8), 16) + 1).toString(16).padStart(8, '0') +
      state.revision.slice(8)
    return Response.json({ success: true, message: 'Artwork saved.' })
  }
  return originalFetch(input, init)
}
await import('../../out/main/index.js')

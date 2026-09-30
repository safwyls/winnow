import { nativeImage } from 'electron'

// The original FullscreenDetailsTests landscape, including its four crop-detection bands.
const width = 1600,
  height = 900
const pixels = Buffer.alloc(width * height * 4)
const mountains = [
  [0, 650],
  [350, 290],
  [610, 570],
  [990, 180],
  [1420, 560],
  [1600, 390],
]
const foreground = [
  [0, 760],
  [260, 550],
  [600, 760],
  [1050, 510],
  [1330, 700],
  [1600, 580],
]
function ridge(points, x) {
  const i = points.findIndex((p) => p[0] > x)
  const [a, b] = [points[i - 1], points[i]]
  return a[1] + ((x - a[0]) / (b[0] - a[0])) * (b[1] - a[1])
}
for (let y = 0; y < height; y++)
  for (let x = 0; x < width; x++) {
    const t = y / height
    const [a, b, f] =
      t <= 0.55
        ? [[32, 62, 96], [215, 163, 124], t / 0.55]
        : [[215, 163, 124], [23, 67, 75], (t - 0.55) / 0.45]
    let color = a.map((channel, index) => Math.round(channel + (b[index] - channel) * f))
    if ((x - 1200) ** 2 + (y - 210) ** 2 < 70 ** 2) color = [245, 222, 179]
    if (y >= ridge(mountains, x)) color = [36, 73, 84]
    if (y >= ridge(foreground, x)) color = [16, 45, 50]
    if (y < 24) color = [255, 0, 0]
    else if (y >= 876) color = [0, 255, 0]
    else if (x < 24) color = [0, 0, 255]
    else if (x >= 1576) color = [255, 255, 0]
    const offset = (y * width + x) * 4
    pixels[offset] = color[2]
    pixels[offset + 1] = color[1]
    pixels[offset + 2] = color[0]
    pixels[offset + 3] = 255
  }
const png = nativeImage.createFromBitmap(pixels, { width, height }).toPNG()
const state = (globalThis.cinematicFixture = {
  userBackground: true,
  longTitle: false,
  hasJournal: false,
  requests: [],
  reads: 0,
  game: null,
})
const summary = Array(12).fill('Explore the mountain coast and find your way home.').join(' ')
const note = 'Found the mountain camp. Next time, follow the coast toward the lighthouse.'
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1' || (init?.method && init.method !== 'GET'))
    return originalFetch(input, init)
  if (url.pathname === '/api/v1/artwork/image') {
    state.requests.push({
      provider: url.searchParams.get('provider'),
      id: url.searchParams.get('id'),
      width: Number(url.searchParams.get('width')),
    })
    return new Response(png, { headers: { 'content-type': 'image/png' } })
  }
  if (/^\/api\/v1\/works\/\d+\/backdrop$/.test(url.pathname))
    return Response.json({
      candidates: [
        {
          key: state.userBackground
            ? { provider: 'user', id: 'landscape' }
            : { provider: 'steam-hero', id: '42' },
          aspectRatio: state.userBackground ? 16 / 9 : 3840 / 1240,
          fitWholeHero: !state.userBackground,
        },
      ],
      coverKey: null,
    })
  const response = await originalFetch(input, init)
  if (!response.ok) return response
  if (url.pathname === '/api/v1/library') {
    const body = await response.json(),
      source = body.games[0]
    const title = state.longTitle
      ? 'A distant shore: the journey beyond the mountains and the forgotten coast'
      : 'A distant shore'
    const playtimeMinutes = state.hasJournal ? 120 : 0
    const lastPlayedAt = state.hasJournal ? new Date(Date.now() - 5 * 86400000).toISOString() : null
    state.game = {
      ...source,
      title,
      summary,
      playtimeMinutes,
      lastPlayedAt,
      entries: [
        { ...source.entries[0], title, store: 'steam', installed: true, playtimeMinutes, lastPlayedAt },
      ],
    }
    return Response.json({ ...body, games: [state.game], lists: [] })
  }
  if (url.pathname === '/api/v1/library/workspace') {
    const body = await response.json()
    return Response.json({
      ...body,
      identityLinks: [],
      externalIds: [
        ...body.externalIds.filter((id) => id.provider !== 'steam'),
        ...body.releases.map((release) => ({ releaseId: release.id, provider: 'steam', providerId: '42' })),
      ],
    })
  }
  if (/^\/api\/v1\/games\/\d+\/details$/.test(url.pathname)) {
    state.reads++
    const body = await response.json(),
      entry = state.game.entries[0]
    return Response.json({
      ...body,
      events: [],
      ratings: [],
      achievements: [],
      history: {},
      sessions: {},
      images: [{ source: 'igdb', kind: 'screenshot', imageIds: 'detailshot,detailshot2' }],
      journalEntries: state.hasJournal
        ? [
            {
              sessionId: 1,
              ownershipId: entry.ownershipId,
              sessionAt: state.game.lastPlayedAt,
              note,
              rating: null,
            },
          ]
        : [],
    })
  }
  return response
}
await import('../../out/main/index.js')

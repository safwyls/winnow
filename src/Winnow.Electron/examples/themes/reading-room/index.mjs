// No bundler is needed for this example. Use the host's React; never bundle another copy.
const { React, createElement: h, defineTheme, settings } = window.WinnowThemeSDK
const pages = [
  ['discover', 'Reading desk'],
  ['library', 'The index'],
  ['journal', 'Field notes'],
]

function BookCover({ context, game, reason }) {
  const interactionRef = React.useRef(null)
  const { Artwork, ArtworkEffects, GamePreview } = context.components
  const artwork = h(Artwork, { workId: game.workId, className: 'rr-book-art' })
  const cover = h(
    'button',
    {
      ref: interactionRef,
      className: 'rr-book-cover',
      onClick: () => context.openGame(game.workId),
      'aria-label': `View ${game.title}`,
    },
    ArtworkEffects ? h(ArtworkEffects, { interactionRef, effects: { foilMetal: 'gold' } }, artwork) : artwork,
  )
  // These API 1 additions are optional on earlier hosts.
  return GamePreview ? h(GamePreview, { game, reason }, cover) : cover
}

function Shell(context) {
  return h(
    'div',
    { className: `reading-room ${context.mode}` },
    h(
      'header',
      { className: 'rr-masthead' },
      h(
        'button',
        { className: 'rr-name', onClick: () => context.setPage('discover'), 'aria-label': 'Winnow home' },
        'Winnow',
        h('span', null, 'Reading room'),
      ),
      h('p', null, 'There is always another story on the shelf.'),
      h(
        'button',
        { onClick: context.toggleFullscreen },
        context.mode === 'fullscreen' ? 'Leave fullscreen' : 'Enter fullscreen',
      ),
    ),
    h(
      'div',
      { className: 'rr-body' },
      h(
        'nav',
        { className: 'rr-index', 'aria-label': 'Main navigation' },
        h('p', { className: 'rr-label' }, 'Contents'),
        ...pages.map(([page, title]) =>
          h(
            'button',
            {
              key: page,
              onClick: () => context.setPage(page),
              'aria-current': context.page === page ? 'page' : undefined,
            },
            title,
          ),
        ),
        h(
          'div',
          { className: 'rr-index-bottom' },
          h('span', null, `${context.games.length.toLocaleString()} games in your library`),
          h('button', { onClick: () => context.setPage('studio') }, 'Theme Studio'),
          h('button', { onClick: () => context.setPage('settings') }, 'Settings'),
        ),
      ),
      h('main', { id: 'main-content', tabIndex: -1 }, context.children),
    ),
    h('footer', null, h('span', null, 'A room of your own.'), h('span', null, 'Ctrl+K · Find a game')),
  )
}

function Discover(context) {
  const preferences = settings(definition, context.profile)
  const shelf = context.feed?.shelves?.find((item) => item.items.length > 0)
  const entries = shelf?.items.slice(0, preferences.suggestions) ?? []
  return h(
    'section',
    { className: 'rr-desk' },
    h('p', { className: 'rr-label' }, 'Open a new chapter'),
    h('h1', null, 'What will you', h('br'), 'get lost in?'),
    h('p', { className: 'rr-deck' }, shelf?.blurb || 'Let your library lead you somewhere unexpected.'),
    context.loading
      ? h('p', { role: 'status' }, 'Opening your library…')
      : entries.length === 0
        ? h(
            'div',
            { className: 'rr-empty' },
            h('h2', null, 'A quiet desk'),
            h('p', null, 'Recommendations will appear here when your library is ready.'),
            h('button', { onClick: () => context.setPage('library') }, 'Browse your library'),
          )
        : h(
            'div',
            { className: 'rr-suggestions' },
            ...entries.map((item) => {
              const game = context.games.find((candidate) =>
                candidate.entries.some((entry) => entry.releaseId === item.releaseId),
              )
              if (!game) return null
              return h(
                context.components.Impression,
                { key: item.releaseId, releaseId: item.releaseId, shelfId: shelf.id },
                h(
                  'article',
                  { className: 'rr-suggestion' },
                  h(BookCover, { context, game, reason: item.reason }),
                  h('span', { className: 'rr-label' }, game.firstReleaseYear || 'From your library'),
                  h('h2', null, h('button', { onClick: () => context.openGame(game.workId) }, game.title)),
                  h('p', null, item.reason),
                  preferences.showSummary && game.summary
                    ? h('p', { className: 'rr-summary' }, game.summary)
                    : null,
                  h(
                    'button',
                    { className: 'rr-open', onClick: () => context.openGame(game.workId) },
                    'Open this story →',
                  ),
                ),
              )
            }),
          ),
    h(
      'div',
      { className: 'rr-desk-foot' },
      h('span', null, shelf?.title || 'Your recommendations'),
      h('button', { onClick: () => context.setPage('library') }, 'See the whole library →'),
    ),
  )
}

function Library(context) {
  const [search, setSearch] = React.useState('')
  const games = React.useMemo(
    () =>
      context.games
        .filter((game) => game.title.toLocaleLowerCase().includes(search.toLocaleLowerCase()))
        .sort((a, b) => a.title.localeCompare(b.title)),
    [context.games, search],
  )
  return h(
    'section',
    { className: 'rr-library' },
    h('p', { className: 'rr-label' }, 'A personal collection'),
    h('h1', null, 'The index'),
    h(
      'label',
      { className: 'rr-search' },
      h('span', null, 'Find a game'),
      h('input', {
        type: 'search',
        'data-library-search': true,
        value: search,
        onChange: (event) => setSearch(event.target.value),
        placeholder: 'Start with a title…',
      }),
    ),
    h('p', { className: 'rr-label', role: 'status' }, `${games.length.toLocaleString()} entries`),
    games.length
      ? h(
          'ol',
          { className: 'rr-records' },
          ...games.map((game) =>
            h(
              'li',
              { key: game.workId },
              h(
                'button',
                { onClick: () => context.openGame(game.workId) },
                h('span', { className: 'rr-record-year' }, game.firstReleaseYear || '—'),
                h('strong', null, game.title),
                h(
                  'span',
                  { className: 'rr-record-meta' },
                  game.entries
                    .map((entry) => entry.store)
                    .filter((store, index, stores) => stores.indexOf(store) === index)
                    .join(' / '),
                ),
                h('span', { 'aria-hidden': true }, '↗'),
              ),
            ),
          ),
        )
      : h('p', null, context.loading ? 'Opening the index…' : 'No matching games. Try another title.'),
  )
}

const definition = defineTheme({
  apiVersion: 1,
  id: 'reading-room',
  name: 'Reading room',
  Shell,
  Discover,
  Library,
  // Omitted Details, Journal and Settings are supplied by the host.
  settings: [
    { id: 'suggestions', label: 'Stories on the desk', type: 'range', min: 1, max: 6, step: 1, default: 3 },
    {
      id: 'showSummary',
      label: 'Include game descriptions',
      description: 'Add a short excerpt under each recommendation.',
      type: 'toggle',
      default: false,
    },
  ],
})

export default definition

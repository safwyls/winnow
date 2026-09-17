# Winnow promo site

The player page, developer page, documentation guides, and interactive architecture diagram deploy to
https://winnow.gg/ through `.github/workflows/pages.yml`.
GitHub Pages must use **GitHub Actions** as its publishing source in repository
Settings → Pages. No deployment secret is required.

Pull requests that change the site run its static build and asset checks. Once
merged into `main`, site changes deploy automatically. The Promo site workflow
also supports manual runs on `main`.

## Local build

From this directory, run `npm ci` and `npm run build:pages` with Node 22.13 or newer.
Upload only `dist/pages` to Pages. The build renders the player and developer routes plus
`/docs/`, `/docs/setup/`, `/docs/configuration/`, `/docs/plugins/`, and `/docs/plugin-sdk/`.
It arranges routes as directory indexes and checks local HTML/CSS links and HTML anchors.
Missing rendered pages or assets fail the build.

## Documentation

The player page pairs interactive game examples with import, organization, and controller
walkthroughs. Keep copy factual and aligned with shipped behavior; Winnow is free and open
source, supported by voluntary donations. Page-specific layout lives in `app/promo.css`.

The developer page displays the architecture snapshot as a responsive SVG, without a nested
scrolling viewer. Its link opens the original interactive diagram with zoom and navigation.
`scripts/export-architecture.mjs` extracts that SVG from `public/architecture-diagram.html`
during the Pages build. Run `node scripts/export-architecture.mjs` after changing the diagram
when working in the development server.

The home-page hero uses the seven app screenshots in `public/assets/screenshots/`.
`app/HeroCarousel.tsx` keeps their captions and alternative text together. Slides advance
every 5.5 seconds, pause during hover or keyboard focus, and stop after manual selection.
Reduced-motion preferences disable automatic rotation. The fixed image area shows each
complete screenshot without cropping or shifting the surrounding page.

`app/docs/player-content.tsx` contains player walkthroughs; `sdk-content.tsx` contains the
API 1 reference. Shared article components supply navigation, section links, and a browser-local
search index built from the same article content. All prose is prerendered and remains readable
without JavaScript. Search needs JavaScript and sends no queries to a server.
Keep instructions aligned with the application source and `docs/plugins.md`; examples live in
`public/examples/`. New routes must also be listed in `scripts/build-pages.mjs`.

The Pages build uses the domain root for links and bundled assets. Set
`PAGES_BASE_PATH` to a slash-prefixed path without a trailing slash only when
hosting under a repository path instead of the custom domain.

`npm run dev` and `npm run build` retain the Sites development and hosting flow.
The Pages build omits the Cloudflare and Sites plugins because its output is static.
It uses an asset prefix and explicit internal links: this Vinext version's export
does not reliably prerender routes with a framework base path or trailing-slash
redirects. Public fonts and the dragon mask use CSS imports so Vite bundles them.

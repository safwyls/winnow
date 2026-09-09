# Winnow promo site

The player page, developer page, and interactive architecture diagram deploy to
https://winnow.gg/ through `.github/workflows/pages.yml`.
GitHub Pages must use **GitHub Actions** as its publishing source in repository
Settings → Pages. No deployment secret is required.

Pull requests that change the site run its static build and asset checks. Once
merged into `main`, site changes deploy automatically. The Promo site workflow
also supports manual runs on `main`.

## Local build

From this directory, run `npm ci` and `npm run build:pages` with Node 22.13 or newer.
Upload only `dist/pages` to Pages. The build renders both routes, arranges the
developer page as `developers/index.html`, and checks local HTML and CSS links.
Missing rendered pages or assets fail the build.

The Pages build uses the domain root for links and bundled assets. Set
`PAGES_BASE_PATH` to a slash-prefixed path without a trailing slash only when
hosting under a repository path instead of the custom domain.

`npm run dev` and `npm run build` retain the Sites development and hosting flow.
The Pages build omits the Cloudflare and Sites plugins because its output is static.
It uses an asset prefix and explicit internal links: this Vinext version's export
does not reliably prerender routes with a framework base path or trailing-slash
redirects. Public fonts and the dragon mask use CSS imports so Vite bundles them.

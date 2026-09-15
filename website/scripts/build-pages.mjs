import { spawnSync } from 'node:child_process';
import { cpSync, mkdirSync, readFileSync, existsSync, readdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const basePath = process.env.PAGES_BASE_PATH ?? '';
if (basePath !== '' && !/^\/[A-Za-z0-9._-]+(?:\/[A-Za-z0-9._-]+)*$/.test(basePath)) {
  throw new Error('PAGES_BASE_PATH must be empty or a slash-prefixed path without a trailing slash.');
}

const result = spawnSync(process.execPath, ['node_modules/vinext/dist/cli.js', 'build'], {
  stdio: 'inherit',
  env: { ...process.env, WINNOW_GITHUB_PAGES: 'true', NEXT_PUBLIC_BASE_PATH: basePath },
});
if (result.error) throw result.error;
if (result.status !== 0) process.exit(result.status ?? 1);

const output = 'dist/pages';
mkdirSync(output, { recursive: true });
cpSync('public', output, { recursive: true });
// Vinext places assetPrefix in the output tree; Pages supplies that mount path.
cpSync(`dist/client${basePath}/_next`, `${output}/_next`, { recursive: true });
const routes = ['developers', 'docs', 'docs/setup', 'docs/configuration', 'docs/plugins', 'docs/plugin-sdk'];
const pages = [
  ['index.html', 'index.html'], ['404.html', '404.html'],
  ...routes.map(route => [`${route}.html`, `${route}/index.html`]),
];
for (const [page, destination] of pages) {
  mkdirSync(path.dirname(`${output}/${destination}`), { recursive: true });
  cpSync(`dist/client/${page}`, `${output}/${destination}`);
}
writeFileSync(`${output}/.nojekyll`, '');

function verifyUrl(raw, source) {
  if (!raw || /^(?:[a-z]+:|\/\/)/i.test(raw)) return;
  const url = new URL(raw, `https://pages.invalid${basePath}/${source}`);
  if (!url.pathname.startsWith(`${basePath}/`)) throw new Error(`Unprefixed URL ${raw} in ${source}`);
  let target = `${output}/${decodeURIComponent(url.pathname.slice(basePath.length + 1))}`;
  if (url.pathname.endsWith('/')) target += 'index.html';
  if (!existsSync(target)) throw new Error(`Missing asset ${raw} in ${source}`);
  if (url.hash && target.endsWith('.html')) {
    const id = decodeURIComponent(url.hash.slice(1));
    const ids = [...readFileSync(target, 'utf8').matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
    if (!ids.includes(id)) throw new Error(`Missing anchor ${raw} in ${source}`);
  }
}

for (const [, source] of pages) {
  const html = readFileSync(`${output}/${source}`, 'utf8');
  for (const tag of html.matchAll(/<(?:a|link|script|img|iframe|video|source|track)\b[^>]*>/g)) {
    for (const attribute of tag[0].matchAll(/(?:href|src|poster)="([^"]+)"/g)) verifyUrl(attribute[1], source);
  }
}
for (const entry of readdirSync(`${output}/_next`, { recursive: true })) {
  if (!entry.endsWith('.css')) continue;
  const source = `_next/${entry.replaceAll('\\', '/')}`;
  for (const match of readFileSync(`${output}/${source}`, 'utf8').matchAll(/url\(["']?([^)'"\s]+)["']?\)/g)) {
    verifyUrl(match[1], source);
  }
}
console.log(`Verified GitHub Pages output in ${output} (base path: ${basePath || '/'}).`);

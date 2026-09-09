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
for (const page of ['index.html', 'developers.html', '404.html']) {
  const destination = page === 'developers.html' ? 'developers/index.html' : page;
  mkdirSync(path.dirname(`${output}/${destination}`), { recursive: true });
  cpSync(`dist/client/${page}`, `${output}/${destination}`);
}
writeFileSync(`${output}/.nojekyll`, '');

function verifyUrl(raw, source) {
  if (!raw || /^(?:[a-z]+:|\/\/|#)/i.test(raw)) return;
  const url = new URL(raw, `https://pages.invalid${basePath}/${source}`);
  if (!url.pathname.startsWith(`${basePath}/`)) throw new Error(`Unprefixed URL ${raw} in ${source}`);
  let target = `${output}/${decodeURIComponent(url.pathname.slice(basePath.length + 1))}`;
  if (url.pathname.endsWith('/')) target += 'index.html';
  if (!existsSync(target)) throw new Error(`Missing asset ${raw} in ${source}`);
}

for (const source of ['index.html', 'developers/index.html', '404.html']) {
  const html = readFileSync(`${output}/${source}`, 'utf8');
  for (const tag of html.matchAll(/<(?:a|link|script|img|iframe)\b[^>]*>/g)) {
    for (const attribute of tag[0].matchAll(/(?:href|src)="([^"]+)"/g)) verifyUrl(attribute[1], source);
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

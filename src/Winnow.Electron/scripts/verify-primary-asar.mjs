import { extractFile, listPackage } from '@electron/asar'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { resolve, join } from 'node:path'

const [archive, version, commit] = process.argv.slice(2)
if (!archive || !version || !/^[a-f0-9]{40}$/.test(commit ?? ''))
  throw new Error('Missing package verification identity')
const names = listPackage(archive).map((name) => name.replaceAll('\\', '/').replace(/^\//, ''))
for (const name of [
  'package.json',
  'out/main/index.js',
  'out/preload/index.cjs',
  'out/preload/epic.cjs',
  'out/renderer/index.html',
]) {
  if (!names.includes(name)) throw new Error(`Missing ASAR entry: ${name}`)
}
if (!names.some((name) => /^out\/renderer\/assets\/.+\.(?:ttf|woff2?)$/.test(name)))
  throw new Error('Missing bundled renderer fonts')
if (
  names.some((name) =>
    /(?:^|\/)(?:appsettings\.local\.json|[^/]*\.secrets\.json|[^/]*\.db(?:-wal|-shm)?)$/i.test(name),
  )
)
  throw new Error('Local data must not enter the ASAR')
const metadata = JSON.parse(extractFile(archive, 'package.json').toString())
if (
  metadata.name !== 'winnow-electron' ||
  metadata.productName !== 'Winnow' ||
  metadata.version !== version ||
  metadata.main !== 'out/main/index.js'
)
  throw new Error('Incorrect packaged application metadata')
const main = extractFile(archive, join('out', 'main', 'index.js')).toString()
if (!main.includes(`${version}+${commit}`))
  throw new Error('Source identity is not embedded in the main bundle')
const project = fileURLToPath(new URL('..', import.meta.url))
const dependencyNames = Object.keys(
  JSON.parse(readFileSync(resolve(project, 'package.json'), 'utf8')).dependencies,
)
for (const name of dependencyNames) {
  if (!names.includes(`node_modules/${name}/package.json`))
    throw new Error(`Missing production package: ${name}`)
}
console.log(
  `Verified ASAR identity, renderer, preloads, bundled fonts and ${dependencyNames.length} production packages.`,
)

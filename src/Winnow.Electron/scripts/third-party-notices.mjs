import { readFile, readdir, mkdir, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('..', import.meta.url))
const lock = JSON.parse(await readFile(resolve(root, 'package-lock.json'), 'utf8'))
const sections = [
  '# Third-party notices\n\nLicenses for production JavaScript dependencies and bundled fonts. Electron and Chromium notices also accompany the executable.\n',
]
for (const [path, entry] of Object.entries(lock.packages)) {
  if (!path || entry.dev) continue
  const directory = resolve(root, path)
  const manifest = JSON.parse(await readFile(resolve(directory, 'package.json'), 'utf8'))
  const files = (await readdir(directory))
    .filter((name) => /^(licen[sc]e|copying|notice|ofl)(\.|$)/i.test(name))
    .sort()
  sections.push(
    `## ${manifest.name} ${manifest.version}\n\nDeclared license: ${manifest.license ?? entry.license ?? 'See notice'}.\n`,
  )
  if (!files.length) {
    if (manifest.name !== 'react-remove-scroll-bar')
      throw new Error(`Missing license notice: ${manifest.name}`)
    sections.push(await readFile(resolve(root, 'resources/licenses/react-remove-scroll-bar.txt'), 'utf8'))
  }
  for (const name of files)
    sections.push(`### ${name}\n\n${await readFile(resolve(directory, name), 'utf8')}\n`)
}
await mkdir(resolve(root, '.staging'), { recursive: true })
await writeFile(resolve(root, '.staging/THIRD-PARTY-NOTICES.md'), sections.join('\n'))
console.log(
  `Collected notices for ${sections.filter((text) => text.startsWith('## ')).length} production packages.`,
)

import { build } from 'esbuild'
import { _electron as electron } from '@playwright/test'
import electronPath from 'electron'
import { execFileSync } from 'node:child_process'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { closeFixture } from '../tests/electron/fixture-cleanup.ts'

// Optional historical revision measures the original base64 path with the same browser and fixture.
const revision = process.argv[2]
if (process.argv.length > 3 || (revision && !/^[a-zA-Z0-9][a-zA-Z0-9._/-]*$/.test(revision)))
  throw Error('Usage: node scripts/benchmark-artwork.mjs [baseline-git-ref]')
const project = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const root = resolve(project, '../..')
const sourcePath = 'src/Winnow.Electron/src/renderer/components/artwork-decode.ts'
const source = revision
  ? execFileSync('git', ['show', `${revision}:${sourcePath}`], { cwd: root, encoding: 'utf8' })
  : await readFile(join(root, sourcePath), 'utf8')
await mkdir(join(root, '.tmp'), { recursive: true })
const directory = await mkdtemp(join(root, '.tmp/artwork-benchmark-'))
await build({
  stdin: {
    contents: `${source}
      Object.assign(window, { runArtworkBatch: async (source, count) => {
        const decoder = new ArtworkDecoder()
        const start = performance.now()
        let first = 0
        const pixels = await Promise.all(Array.from({ length: count }, () =>
          decoder.decode(source, new AbortController().signal, async () => source, () => {}).then(value => {
            if (!first) first = performance.now() - start
            return value
          })))
        const complete = performance.now() - start
        const valid = pixels.filter(Boolean).length
        pixels.forEach(value => value?.dispose())
        decoder.close()
        return { first, complete, valid }
      }})`,
    resolveDir: dirname(join(root, sourcePath)),
    loader: 'ts',
  },
  outfile: join(directory, 'renderer.js'),
  bundle: true,
  platform: 'browser',
  format: 'iife',
  logLevel: 'silent',
})
await writeFile(
  join(directory, 'index.html'),
  `<!doctype html><html><head>
  <meta http-equiv="Content-Security-Policy" content="default-src 'self'; script-src 'self'; img-src 'self' blob: data:">
  </head><body><script src="renderer.js"></script></body></html>`,
)
const userData = join(directory, 'profile')
await mkdir(userData)
let application
try {
  application = await electron.launch({
    executablePath: electronPath,
    args: [
      join(project, 'tests/electron/actions-probe-main.mjs'),
      '--data-dir',
      userData,
      join(directory, 'index.html'),
    ],
    env: Object.fromEntries(Object.entries(process.env).filter(([key]) => key !== 'ELECTRON_RUN_AS_NODE')),
    chromiumSandbox: true,
  })
  const page = await application.firstWindow()
  await page.waitForFunction(() => typeof window.runArtworkBatch === 'function')
  const results = []
  for (const scenario of [
    { name: '24 covers', width: 240, height: 360, count: 24 },
    { name: 'screenshot', width: 1280, height: 720, count: 1 },
    { name: '3 backdrops', width: 1920, height: 1080, count: 3 },
  ]) {
    // Generate identical textured PNGs outside the timed region, without disk or network variance.
    await page.evaluate(({ width, height }) => {
      const canvas = document.createElement('canvas')
      canvas.width = width
      canvas.height = height
      const context = canvas.getContext('2d')
      const pixels = context.createImageData(width, height)
      let seed = 12345
      for (let i = 0; i < pixels.data.length; i += 4) {
        seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0
        pixels.data[i] = seed & 255
        pixels.data[i + 1] = (seed >>> 8) & 255
        pixels.data[i + 2] = (seed >>> 16) & 255
        pixels.data[i + 3] = 255
      }
      context.putImageData(pixels, 0, 0)
      window.artworkSource = canvas.toDataURL('image/png')
      window.artworkBytes = Uint8Array.from(atob(window.artworkSource.split(',')[1]), (c) => c.charCodeAt(0))
    }, scenario)
    for (const binary of revision ? [false] : [false, true]) {
      const runs = []
      for (let trial = 0; trial < 6; trial++) {
        const run = await page.evaluate(
          ({ count, binary }) =>
            window.runArtworkBatch(binary ? window.artworkBytes : window.artworkSource, count),
          { count: scenario.count, binary },
        )
        if (run.valid !== scenario.count) throw Error(`Image decode failed in ${scenario.name}`)
        if (trial) runs.push(run)
      }
      const medianMs = runs.map((run) => run.complete).sort((a, b) => a - b)[2]
      results.push({
        ...scenario,
        binary,
        bytes: await page.evaluate(() => window.artworkBytes.byteLength),
        medianMs,
        runs,
      })
      console.log(`${scenario.name}, ${binary ? 'binary' : 'base64'}: ${medianMs.toFixed(1)}ms`)
    }
  }
  const engine = await application.evaluate(() => ({
    electron: process.versions.electron,
    chromium: process.versions.chrome,
  }))
  await writeFile(
    join(directory, 'results.json'),
    JSON.stringify({ revision: revision ?? 'working tree', engine, results }, null, 2),
  )
  console.log(`Results: ${join(directory, 'results.json')}`)
} finally {
  await closeFixture(application)
}

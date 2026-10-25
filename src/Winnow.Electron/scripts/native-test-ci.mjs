import { spawn, execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdir, mkdtemp, readFile, readdir, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { resolve, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createEvidence, verifyEvidence } from './native-test-evidence.mjs'
import { withRetainedDiagnostics } from './native-test-diagnostics.mjs'

const root = fileURLToPath(new URL('../../../', import.meta.url))
const frontend = join(root, 'src/Winnow.Electron')
const [mode, argument, count] = process.argv.slice(2)
const json = async (path) => JSON.parse(await readFile(path, 'utf8'))
if (mode === 'verify') {
  const directory = resolve(argument)
  const files = (await readdir(directory, { recursive: true })).filter((name) =>
    name.endsWith('native-evidence.json'),
  )
  const summary = verifyEvidence(
    await Promise.all(files.map((file) => json(join(directory, file)))),
    Number(count),
  )
  console.log(JSON.stringify(summary, null, 2))
} else if (mode === 'run') {
  const [current, total] = argument.split('/').map(Number)
  if (!Number.isInteger(current) || !Number.isInteger(total) || current < 1 || current > total)
    throw Error('Use run <shard>/<total>.')
  const directory = join(root, '.tmp', `electron-native-${current}`)
  await mkdir(join(root, '.tmp'), { recursive: true })
  // A fresh retained directory cannot accidentally reuse a prior passing receipt.
  await mkdir(directory)
  const rawDirectory = await mkdtemp(join(tmpdir(), `winnow-native-${current}-`))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key]) => !/^(ELECTRON_RUN_AS_NODE|ELECTRON_RENDERER_URL|Igdb__(ClientId|ClientSecret))$/i.test(key),
    ),
  )
  const run = (name, args) =>
    new Promise((done, reject) => {
      const child = spawn(
        process.execPath,
        [
          join(frontend, 'node_modules/@playwright/test/cli.js'),
          'test',
          '--workers=1',
          '--forbid-only',
          '--retries=0',
          ...args,
        ],
        {
          cwd: frontend,
          windowsHide: true,
          stdio: 'inherit',
          env: {
            ...environment,
            CI: 'true',
            Igdb__ClientId: '',
            Igdb__ClientSecret: '',
            PLAYWRIGHT_JSON_OUTPUT_NAME: join(rawDirectory, `${name}.json`),
          },
        },
      )
      child.once('error', reject)
      child.once('exit', (code) =>
        code === 0 ? done() : reject(Error(`Playwright ${name} exited ${code}.`)),
      )
    })
  const evidence = await withRetainedDiagnostics(rawDirectory, directory, async () => {
    await run('full-inventory', ['--list', '--reporter=json'])
    await run('shard-inventory', ['--list', `--shard=${argument}`, '--reporter=json'])
    await run('results', [
      `--shard=${argument}`,
      '--reporter=list,json',
      `--output=${join(rawDirectory, 'artifacts')}`,
    ])
    const command = (exe, args) =>
      execFileSync(exe, args, { cwd: root, windowsHide: true, encoding: 'utf8' }).trim()
    const provenance = {
      commit: command('git', ['rev-parse', 'HEAD']),
      tree: command('git', ['rev-parse', 'HEAD^{tree}']),
      lockfile: createHash('sha256')
        .update(await readFile(join(frontend, 'package-lock.json')))
        .digest('hex'),
      node: process.version,
      electron: (await json(join(frontend, 'node_modules/electron/package.json'))).version,
      sdk: command('dotnet', ['--version']),
      platform: process.platform,
      imageOS: process.env.ImageOS ?? 'local',
      imageVersion: process.env.ImageVersion ?? 'local',
    }
    if (process.env.GITHUB_SHA && provenance.commit !== process.env.GITHUB_SHA)
      throw Error('Checkout differs from the workflow SHA.')
    return createEvidence(
      await json(join(rawDirectory, 'full-inventory.json')),
      await json(join(rawDirectory, 'shard-inventory.json')),
      await json(join(rawDirectory, 'results.json')),
      provenance,
      { current, total },
    )
  })
  await writeFile(join(directory, 'native-evidence.json'), JSON.stringify(evidence, null, 2) + '\n')
  console.log(`Native shard ${current}/${total}: ${evidence.results.length} tests passed once.`)
} else throw Error('Use run <shard>/<total> or verify <evidence-directory> <total>.')

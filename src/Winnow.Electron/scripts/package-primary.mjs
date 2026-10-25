import { execFileSync, spawnSync } from 'node:child_process'
import { resolve, relative, isAbsolute } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parseArgs } from 'node:util'
import { checkRepositoryVersion, repositoryRoot } from './check-version.mjs'

export function packagePlan(argv, context) {
  const { values, positionals } = parseArgs({
    args: argv,
    allowPositionals: true,
    strict: true,
    options: Object.fromEntries(
      ['runtime', 'version', 'commit', 'output', 'publish-directory'].map((key) => [key, { type: 'string' }]),
    ),
  })
  const mode = positionals[0]
  if (positionals.length !== 1 || !['package', 'dist'].includes(mode))
    throw Error('Use package or dist, with optional --runtime, --version, --commit and --output.')
  const hostRuntime = { win32: 'win-x64', linux: 'linux-x64' }[context.platform]
  const runtime = values.runtime ?? hostRuntime
  if (context.arch !== 'x64' || !hostRuntime || runtime !== hostRuntime)
    throw Error('Primary packages require their matching Windows x64 or Linux x64 host.')
  const version = values.version ?? context.version
  const commit = values.commit ?? context.commit
  if (
    !/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/.test(
      version ?? '',
    ) ||
    !/^[0-9a-f]{40}$/i.test(commit ?? '')
  )
    throw Error('Packaging requires a SemVer version and full 40-character source commit.')
  if (mode === 'package' && values['publish-directory'])
    throw Error('package uses --output for its verified directory; --publish-directory is a dist option.')
  const defaultPublish = resolve(context.root, 'artifacts/electron-publish', runtime)
  const publish =
    mode === 'package'
      ? values.output
        ? resolve(context.cwd, values.output)
        : defaultPublish
      : values['publish-directory']
        ? resolve(context.cwd, values['publish-directory'])
        : defaultPublish
  const powershell = (script, args) => ({
    executable: 'pwsh',
    args: ['-NoProfile', '-File', resolve(context.root, script), ...args],
    cwd: context.root,
  })
  const commands = [
    powershell('packaging/Publish.ps1', [
      '-Runtime',
      runtime,
      '-Version',
      version,
      '-Commit',
      commit,
      '-OutputDirectory',
      publish,
    ]),
  ]
  if (mode === 'dist') {
    const output = values.output
      ? resolve(context.cwd, values.output)
      : resolve(context.root, 'artifacts/electron-packages')
    const inside = (child, parent) => {
      const path = relative(parent, child)
      return !path || (!path.startsWith('..') && !isAbsolute(path))
    }
    if (inside(output, publish) || inside(publish, output))
      throw Error('Distribution archives and the verified publish directory must not overlap.')
    commands.push(
      runtime === 'win-x64'
        ? powershell('packaging/windows/New-WindowsPackage.ps1', [
            '-PublishDirectory',
            publish,
            '-OutputDirectory',
            output,
            '-Version',
            version,
          ])
        : {
            executable: 'bash',
            args: [resolve(context.root, 'packaging/linux/build.sh'), publish, output, version],
            cwd: context.root,
          },
    )
  }
  return commands
}

export function executePackagePlan(commands, run = spawnSync) {
  for (const command of commands) {
    const result = run(command.executable, command.args, {
      cwd: command.cwd,
      stdio: 'inherit',
      windowsHide: true,
    })
    if (result.error) throw result.error
    if (result.status !== 0)
      throw Error(`${command.executable} failed (${result.status ?? result.signal ?? 'no exit code'}).`)
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const argv = process.argv.slice(2)
    const version = checkRepositoryVersion()
    const commit = argv.some((value) => value === '--commit' || value.startsWith('--commit='))
      ? undefined
      : execFileSync('git', ['rev-parse', 'HEAD'], {
          cwd: repositoryRoot,
          encoding: 'utf8',
          windowsHide: true,
        }).trim()
    executePackagePlan(
      packagePlan(argv, {
        root: repositoryRoot,
        cwd: process.cwd(),
        platform: process.platform,
        arch: process.arch,
        version,
        commit,
      }),
    )
  } catch (error) {
    console.error(error.message)
    process.exitCode = 1
  }
}

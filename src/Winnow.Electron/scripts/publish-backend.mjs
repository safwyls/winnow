import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'

const root = fileURLToPath(new URL('..', import.meta.url))
const os = { win32: 'win', linux: 'linux', darwin: 'osx' }[process.platform]
if (!os || !['x64', 'arm64'].includes(process.arch)) throw new Error('Unsupported packaging platform')
const result = spawn(
  'dotnet',
  [
    'publish',
    resolve(root, '../Winnow.Backend/Winnow.Backend.csproj'),
    '-c',
    'Release',
    '-r',
    `${os}-${process.arch}`,
    '--self-contained',
    'true',
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-o',
    resolve(root, '.staging/backend'),
    `-p:BaseOutputPath=${resolve(root, '.staging/dotnet-bin')}/`,
  ],
  { cwd: root, stdio: 'inherit', windowsHide: true },
)
result.once('error', (error) => {
  console.error(error.message)
  process.exitCode = 1
})
result.once('exit', (code) => {
  process.exitCode = code ?? 1
})

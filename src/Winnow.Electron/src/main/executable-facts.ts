import { execFile } from 'node:child_process'
import { posix, win32 } from 'node:path'
import type { ExecutableFacts, ExecutableTitleSource } from '../shared/executable-facts'

const buildSuffixes = [
  '-Win64-Shipping',
  '-Win32-Shipping',
  '-WinGDK-Shipping',
  '-Shipping',
  '-Win64',
  '-Win32',
  '_Win64',
  '_Win32',
  '-x64',
  '-x86',
  '_x64',
  '_x86',
  ' x64',
  ' x86',
  ' (64-bit)',
  ' (32-bit)',
  ' (64 bit)',
  ' (32 bit)',
  ' Launcher',
  '-Launcher',
  '_Launcher',
  '_Data',
]
const stubs = new Set(
  (
    'app appdata application apps bin binaries binary build builds client common content data debug ' +
    'desktop dist documents downloads engine executable exe files game gamedata games install installer ' +
    'launch launcher local main newfolder output play program programfiles programfilesx86 programs redist ' +
    'release roaming run setup shipping src start startup steamapps system32 temp tmp uninstall uninstaller ' +
    'unins000 users win win32 win64 windows x64 x86 defaultcompany dosbox electron emulator epicgames ' +
    'galaxy gamemaker godot gog java javaw node ue4 ue5 nodejs python pythonw retroarch rpgmaker scummvm steam unity unityplayer wine'
  ).split(' '),
)

function usable(raw: string | null | undefined): string | null {
  if (!raw?.trim()) return null
  let value = raw.trim().replace(/\.exe$/i, '')
  let previous: string
  do {
    previous = value
    for (const suffix of buildSuffixes)
      if (value.length > suffix.length && value.toLowerCase().endsWith(suffix.toLowerCase()))
        value = value.slice(0, -suffix.length)
  } while (value !== previous)
  value = value
    .replaceAll('_', ' ')
    .replace(/\s+/gu, ' ')
    .replace(/^[ -]+|[ -]+$/g, '')
  const key = value.replace(/[^\p{L}\p{N}]/gu, '').toLowerCase()
  return key.length < 2 || /^\d+$/.test(key) || stubs.has(key) || key.startsWith('unrealengine')
    ? null
    : value
}

/** Mirrors the original five-level, description/product/folder/file proposal order. */
export function deriveExecutableFacts(
  executablePath: string,
  fileDescription?: string | null,
  productName?: string | null,
  companyName?: string | null,
): ExecutableFacts {
  if (!executablePath.trim()) throw Error('Choose an executable path.')
  const selected = executablePath.trim()
  const path = /\\|^[a-z]:/i.test(selected) ? win32 : posix
  const directory = path.dirname(selected)
  const installPath = directory === '.' ? null : directory
  const candidates: [ExecutableTitleSource, string | null | undefined][] = [
    ['file-description', fileDescription],
    ['product-name', productName],
  ]
  let current = installPath
  for (let depth = 0; depth < 5 && current; depth++) {
    const normalized = current.replace(/[\\/]+$/, '')
    if (
      normalized.toLowerCase() ===
      path
        .parse(current)
        .root.replace(/[\\/]+$/, '')
        .toLowerCase()
    )
      break
    candidates.push(['folder-name', path.basename(normalized)])
    const parent = path.dirname(current)
    current = parent === current || parent === '.' ? null : parent
  }
  candidates.push(['file-name', path.basename(selected, path.extname(selected))])
  for (const [titleSource, candidate] of candidates) {
    const title = usable(candidate)
    if (title)
      return { executablePath: selected, installPath, title, titleSource, publisher: usable(companyName) }
  }
  return {
    executablePath: selected,
    installPath,
    title: null,
    titleSource: 'none',
    publisher: usable(companyName),
  }
}

interface VersionFields {
  fileDescription?: string | null
  productName?: string | null
  companyName?: string | null
}
// The path is stdin data, never PowerShell source. FileVersionInfo reads the PE
// resource section without loading or executing the selected image.
const versionScript = [
  '$ErrorActionPreference = "Stop"',
  '[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)',
  '[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)',
  '$selectedPath = [Console]::In.ReadToEnd()',
  '$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($selectedPath)',
  '@{fileDescription=$version.FileDescription;productName=$version.ProductName;companyName=$version.CompanyName} | ConvertTo-Json -Compress',
].join('; ')

export function readExecutableVersion(executablePath: string): Promise<VersionFields> {
  if (process.platform !== 'win32') return Promise.resolve({})
  const shell = win32.join(
    process.env.SystemRoot ?? 'C:\\Windows',
    'System32',
    'WindowsPowerShell',
    'v1.0',
    'powershell.exe',
  )
  return new Promise((resolve) => {
    const child = execFile(
      shell,
      [
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-EncodedCommand',
        Buffer.from(versionScript, 'utf16le').toString('base64'),
      ],
      { windowsHide: true, timeout: 5000, maxBuffer: 64 * 1024, encoding: 'utf8' },
      (error, stdout) => {
        if (error) {
          resolve({})
          return
        }
        try {
          const parsed = JSON.parse(stdout.replace(/^\uFEFF/, '')) as Record<string, unknown>
          const field = (name: string) =>
            typeof parsed[name] === 'string' ? (parsed[name] as string).slice(0, 2048) : null
          resolve({
            fileDescription: field('fileDescription'),
            productName: field('productName'),
            companyName: field('companyName'),
          })
        } catch {
          resolve({})
        }
      },
    )
    child.stdin?.on('error', () => {
      /* Exit before stdin drains still resolves through execFile. */
    })
    child.stdin?.end(executablePath, 'utf8')
  })
}

export async function inspectExecutable(
  executablePath: string,
  read = readExecutableVersion,
): Promise<ExecutableFacts> {
  let version: VersionFields = {}
  try {
    version = await read(executablePath)
  } catch {
    /* Unreadable resources retain the path proposal. */
  }
  return deriveExecutableFacts(
    executablePath,
    version.fileDescription,
    version.productName,
    version.companyName,
  )
}

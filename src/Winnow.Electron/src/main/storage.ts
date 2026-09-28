import { createHash, randomUUID } from 'node:crypto'
import { lstat, mkdir, readFile, readdir, realpath, rename, rm, stat, writeFile } from 'node:fs/promises'
import { basename, dirname, extname, join, relative, resolve } from 'node:path'
import type { ThemePackage } from '../shared/bridge'
import { containedPath } from './security'

const maxJson = 512 * 1024
export function profileDirectory(userData: string, dataDirectory: string): string {
  const canonical =
    process.platform === 'win32' ? resolve(dataDirectory).toLowerCase() : resolve(dataDirectory)
  return join(userData, 'libraries', createHash('sha256').update(canonical).digest('hex').slice(0, 24))
}
export function serializeProfile(value: unknown): string {
  const json = JSON.stringify(value, null, 2)
  if (
    !value ||
    typeof value !== 'object' ||
    Array.isArray(value) ||
    !json ||
    Buffer.byteLength(json) > maxJson
  )
    throw new Error('Profile must be a JSON object smaller than 512 KB')
  return json
}
export async function readProfile(path: string): Promise<unknown> {
  if ((await stat(path)).size > maxJson) throw new Error('Profile is too large')
  const value: unknown = JSON.parse(await readFile(path, 'utf8'))
  serializeProfile(value)
  return value
}
export async function saveProfile(path: string, value: unknown): Promise<void> {
  const json = serializeProfile(value)
  await mkdir(dirname(path), { recursive: true })
  const temporary = `${path}.${randomUUID()}.tmp`
  await writeFile(temporary, json, { mode: 0o600, flag: 'wx' })
  try {
    await rename(temporary, path)
  } finally {
    await rm(temporary, { force: true })
  }
}

export interface ThemeManifest {
  id: string
  name: string
  version: string
  apiVersion: number
  description?: string
  entry: string
  css?: string
}
export function validateManifest(value: unknown): ThemeManifest {
  if (!value || typeof value !== 'object') throw new Error('Missing theme manifest')
  const manifest = value as ThemeManifest
  if (manifest.apiVersion !== 1) throw new Error('This theme requires a different frontend API version')
  if (
    typeof manifest.id !== 'string' ||
    manifest.id.length > 80 ||
    !/^[a-z0-9]+(?:[.-][a-z0-9]+)*$/.test(manifest.id) ||
    ['afterglow', 'rift', 'catalogue', 'index'].includes(manifest.id) ||
    /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/.test(manifest.id)
  )
    throw new Error('Invalid or reserved theme ID')
  if (typeof manifest.name !== 'string' || !manifest.name.trim() || manifest.name.length > 120)
    throw new Error('Invalid theme name')
  if (typeof manifest.version !== 'string' || !/^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$/.test(manifest.version))
    throw new Error('Theme version must use major.minor.patch')
  for (const [key, value] of [
    ['entry', manifest.entry],
    ['css', manifest.css],
  ] as const) {
    if (key === 'css' && value === undefined) continue
    if (
      typeof value !== 'string' ||
      value.length > 240 ||
      !/^[a-zA-Z0-9_./-]+$/.test(value) ||
      value.split('/').some((part) => !part || part === '.' || part === '..') ||
      value.includes(':')
    )
      throw new Error(`Invalid theme ${key}`)
    if (key === 'entry' && !/\.(m?js)$/.test(value))
      throw new Error('Theme entry must be a JavaScript module')
    if (key === 'css' && !value.endsWith('.css')) throw new Error('Theme stylesheet must be CSS')
  }
  if (
    manifest.description !== undefined &&
    (typeof manifest.description !== 'string' || manifest.description.length > 1000)
  )
    throw new Error('Invalid theme description')
  return {
    id: manifest.id,
    name: manifest.name,
    version: manifest.version,
    apiVersion: 1,
    description: manifest.description,
    entry: manifest.entry,
    css: manifest.css,
  }
}
export async function readManifest(directory: string): Promise<ThemeManifest> {
  const path = join(directory, 'theme.json')
  if (
    (await lstat(directory)).isSymbolicLink() ||
    (await lstat(path)).isSymbolicLink() ||
    (await stat(path)).size > 16384
  )
    throw new Error('Invalid theme manifest')
  return validateManifest(JSON.parse(await readFile(path, 'utf8')))
}
export function packageDescription(manifest: ThemeManifest): ThemePackage {
  const prefix = `winnow-theme://${manifest.id}/`
  const version = `?v=${encodeURIComponent(manifest.version)}`
  return {
    ...manifest,
    entry: `${prefix}${manifest.entry}${version}`,
    css: manifest.css ? `${prefix}${manifest.css}${version}` : undefined,
  }
}
const extensions = new Set([
  '.js',
  '.mjs',
  '.css',
  '.json',
  '.svg',
  '.png',
  '.jpg',
  '.jpeg',
  '.webp',
  '.woff2',
  '.woff',
])
export async function installThemeDirectory(source: string, themesRoot: string): Promise<ThemePackage> {
  const manifest = await readManifest(source)
  const root = await realpath(source)
  const files: { relativePath: string; bytes: Buffer }[] = []
  let total = 0
  let directories = 0
  async function scan(directory: string, depth = 0): Promise<void> {
    if (++directories > 512 || depth > 16) throw new Error('Theme contains too many nested directories')
    for (const item of await readdir(directory, { withFileTypes: true })) {
      if (item.name.startsWith('.')) continue
      const path = join(directory, item.name)
      const info = await lstat(path)
      if (info.isSymbolicLink() || (await realpath(path)) !== path)
        throw new Error('Theme packages cannot contain symbolic links')
      if (item.isDirectory()) {
        await scan(path, depth + 1)
        continue
      }
      if (!info.isFile() || !extensions.has(extname(item.name).toLowerCase()))
        throw new Error(`Unsupported theme file: ${item.name}`)
      if (files.length >= 512 || total + info.size > 32 * 1024 * 1024)
        throw new Error('Theme exceeds 512 files or 32 MB')
      const bytes = await readFile(path)
      total += bytes.byteLength
      if (total > 32 * 1024 * 1024) throw new Error('Theme exceeds 32 MB')
      files.push({ relativePath: relative(root, path).replaceAll('\\', '/'), bytes })
    }
  }
  await scan(root)
  if (
    !files.some((file) => file.relativePath === manifest.entry) ||
    (manifest.css && !files.some((file) => file.relativePath === manifest.css))
  )
    throw new Error('Theme entry or stylesheet is missing')
  await mkdir(themesRoot, { recursive: true })
  const staging = containedPath(themesRoot, `staging-${randomUUID()}`)
  const destination = containedPath(themesRoot, manifest.id)
  const backup = containedPath(themesRoot, `backup-${randomUUID()}`)
  await mkdir(staging)
  let backedUp = false
  try {
    for (const file of files) {
      const path = containedPath(staging, file.relativePath)
      await mkdir(dirname(path), { recursive: true })
      await writeFile(path, file.bytes, { mode: 0o600, flag: 'wx' })
    }
    try {
      await rename(destination, backup)
      backedUp = true
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error
    }
    try {
      await rename(staging, destination)
    } catch (error) {
      if (backedUp) await rename(backup, destination)
      throw error
    }
    if (backedUp) await rm(backup, { recursive: true, force: true })
    return packageDescription(manifest)
  } finally {
    await rm(staging, { recursive: true, force: true })
  }
}
export async function listThemePackages(themesRoot: string): Promise<ThemePackage[]> {
  await mkdir(themesRoot, { recursive: true })
  const result: ThemePackage[] = []
  for (const directory of await readdir(themesRoot, { withFileTypes: true })) {
    if (
      !directory.isDirectory() ||
      directory.name.startsWith('staging-') ||
      directory.name.startsWith('backup-')
    )
      continue
    try {
      const manifest = await readManifest(join(themesRoot, directory.name))
      if (manifest.id === directory.name) result.push(packageDescription(manifest))
    } catch {
      /* Invalid packages are excluded so users can always launch the bundled frontend. */
    }
  }
  return result.sort((left, right) => left.name.localeCompare(right.name))
}
export async function themeFile(
  themesRoot: string,
  urlValue: string,
): Promise<{ bytes: Buffer; type: string }> {
  const url = new URL(urlValue)
  if (url.protocol !== 'winnow-theme:' || url.username || url.password || url.port)
    throw new Error('Invalid theme URL')
  const root = containedPath(themesRoot, url.hostname)
  const manifest = await readManifest(root)
  if (manifest.id !== url.hostname) throw new Error('Unknown theme')
  const decoded = decodeURIComponent(url.pathname.slice(1))
  const path = containedPath(root, decoded)
  const canonical = await realpath(path)
  if (canonical !== path || (await lstat(path)).isSymbolicLink()) throw new Error('Invalid theme file')
  if ((await stat(path)).size > 32 * 1024 * 1024) throw new Error('Theme file is too large')
  const types: Record<string, string> = {
    '.js': 'text/javascript',
    '.mjs': 'text/javascript',
    '.css': 'text/css',
    '.json': 'application/json',
    '.svg': 'image/svg+xml',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.webp': 'image/webp',
    '.woff2': 'font/woff2',
    '.woff': 'font/woff',
  }
  const type = types[extname(basename(path))]
  if (!type) throw new Error('Unsupported theme file')
  return { bytes: await readFile(path), type }
}

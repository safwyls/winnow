import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

export const repositoryRoot = fileURLToPath(new URL('../../..', import.meta.url))

export function assertRepositoryVersions(props, manifest, lock) {
  const prefix = props.match(/<VersionPrefix>([^<]+)<\/VersionPrefix>/)?.[1]
  const suffix = props.match(/<VersionSuffix\b[^>]*>([^<]+)<\/VersionSuffix>/)?.[1]
  if (!prefix || !suffix) throw Error('Version.props must declare the repository development identity.')
  const version = `${prefix}-${suffix}`
  if ([manifest.version, lock.version, lock.packages?.['']?.version].some((value) => value !== version))
    throw Error(`Frontend package.json and package-lock.json must match Version.props: ${version}.`)
  return version
}

export function checkRepositoryVersion(root = repositoryRoot) {
  return assertRepositoryVersions(
    readFileSync(resolve(root, 'Version.props'), 'utf8'),
    JSON.parse(readFileSync(resolve(root, 'src/Winnow.Electron/package.json'), 'utf8')),
    JSON.parse(readFileSync(resolve(root, 'src/Winnow.Electron/package-lock.json'), 'utf8')),
  )
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  console.log(`Repository application version: ${checkRepositoryVersion()}`)

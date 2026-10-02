import packageMetadata from '../../package.json'

declare const __WINNOW_BUILD_INFORMATIONAL_VERSION__: string

export interface ApplicationBuildInfo {
  version: string
  commit: string
}

export function fromInformationalVersion(value: string | null | undefined): ApplicationBuildInfo {
  if (!value?.trim()) return { version: 'Unknown', commit: 'Unavailable' }
  const separator = value.indexOf('+')
  return {
    version: separator < 0 ? value : value.slice(0, separator),
    commit: separator >= 0 && separator < value.length - 1 ? value.slice(separator + 1) : 'Unavailable',
  }
}

export function applicationBuildInfo(packaged: boolean, packagedVersion?: string): ApplicationBuildInfo {
  const embedded = fromInformationalVersion(
    typeof __WINNOW_BUILD_INFORMATIONAL_VERSION__ === 'string'
      ? __WINNOW_BUILD_INFORMATIONAL_VERSION__
      : packageMetadata.version,
  )
  return packaged && packagedVersion?.trim()
    ? { ...embedded, version: fromInformationalVersion(packagedVersion).version }
    : embedded
}

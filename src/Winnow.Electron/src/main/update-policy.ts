export const updateRepository = 'https://github.com/safwyls/winnow'

export function releaseVersion(value: string): { core: bigint[]; pre: string[] } | null {
  const match = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/.exec(
    value,
  )
  if (!match) return null
  const pre = match[4]?.split('.') ?? []
  if (pre.some((value) => /^\d+$/.test(value) && value.length > 1 && value[0] === '0')) return null
  return { core: match.slice(1, 4).map(BigInt), pre }
}
export function newerRelease(candidate: string, current: string, includeBeta: boolean): boolean {
  const a = releaseVersion(candidate),
    b = releaseVersion(current)
  if (!a || !b || (!includeBeta && a.pre.length) || a.pre.some((value) => /^(dev|ci)$/i.test(value)))
    return false
  for (let i = 0; i < 3; i++) if (a.core[i] !== b.core[i]) return a.core[i] > b.core[i]
  if (!a.pre.length || !b.pre.length) return !a.pre.length && !!b.pre.length
  for (let i = 0; i < Math.max(a.pre.length, b.pre.length); i++) {
    const left = a.pre[i],
      right = b.pre[i]
    if (left === right) continue
    if (left === undefined || right === undefined) return right === undefined
    const numericLeft = /^\d+$/.test(left),
      numericRight = /^\d+$/.test(right)
    if (numericLeft !== numericRight) return !numericLeft
    return numericLeft ? BigInt(left) > BigInt(right) : left > right
  }
  return false
}
export function electronAssetName(version: string, platform: string, arch: string): string {
  if (!releaseVersion(version) || !['x64', 'arm64', 'ia32'].includes(arch))
    throw new Error('Unsupported update architecture or version.')
  if (platform === 'win32') return `Winnow-Electron-${version}-win-${arch}-Setup.exe`
  if (platform === 'linux') return `Winnow-Electron-${version}-linux-${arch}.AppImage`
  throw new Error('This installation requires a manual update.')
}
export function validateUpdateFiles(
  info: { version: string; files?: { url: string; sha512: string; size?: number }[]; packages?: unknown },
  platform: string,
  arch: string,
): void {
  const expected = electronAssetName(info.version, platform, arch)
  if (!info.files?.length || info.files.length > 16 || info.packages)
    throw new Error('Incomplete or unsupported update metadata.')
  let found = false
  for (const file of info.files) {
    // A channel can contain several architectures. Every entry must still be an Electron artifact.
    const name = file.url.split('/').at(-1)!
    if (!['x64', 'arm64', 'ia32'].some((value) => name === electronAssetName(info.version, platform, value)))
      throw new Error('The release does not contain an Electron update.')
    if (file.url !== name && file.url !== `${updateRepository}/releases/download/v${info.version}/${name}`)
      throw new Error('Untrusted update download location.')
    if (
      !/^[A-Za-z0-9+/]{86}==$/.test(file.sha512) ||
      (file.size !== undefined &&
        (!Number.isSafeInteger(file.size) || file.size <= 0 || file.size > 4 * 1024 ** 3))
    )
      throw new Error('Invalid update checksum or size.')
    if (name === expected) found = true
  }
  if (!found) throw new Error('The release has no update for this architecture.')
}

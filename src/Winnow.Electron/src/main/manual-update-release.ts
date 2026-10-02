import type { UpdateRelease } from './application-updater'
import { electronAssetName, newerRelease, updateRepository } from './update-policy'

export async function manualRelease(
  version: string,
  includeBeta: boolean,
  platform: string,
  arch: string,
  signal?: AbortSignal,
): Promise<UpdateRelease | null> {
  let selected = version,
    bytes = 0
  const lifetime = AbortSignal.timeout(30_000)
  for (let page = 1; page <= 10; page++) {
    const response = await fetch(
      `https://api.github.com/repos/safwyls/winnow/releases?per_page=100&page=${page}`,
      {
        headers: { Accept: 'application/vnd.github+json' },
        redirect: 'error',
        signal: signal ? AbortSignal.any([signal, lifetime]) : lifetime,
      },
    )
    if (!response.ok) throw new Error('The releases could not be read.')
    const text = await response.text()
    bytes += Buffer.byteLength(text)
    if (bytes > 4 * 1024 ** 2) throw new Error('The release list is too large.')
    const releases: unknown = JSON.parse(text)
    if (!Array.isArray(releases) || releases.length > 100) throw new Error('Invalid releases.')
    for (const entry of releases) {
      if (!entry || typeof entry !== 'object') continue
      const release = entry as { tag_name?: unknown; draft?: unknown; prerelease?: unknown; assets?: unknown }
      if (
        release.draft !== false ||
        typeof release.prerelease !== 'boolean' ||
        (!includeBeta && release.prerelease) ||
        typeof release.tag_name !== 'string' ||
        !release.tag_name.startsWith('v')
      )
        continue
      const candidate = release.tag_name.slice(1)
      if (!newerRelease(candidate, selected, includeBeta) || !Array.isArray(release.assets)) continue
      const expected =
        platform === 'darwin' && ['x64', 'arm64'].includes(arch)
          ? `Winnow-Electron-${candidate}-mac-${arch}.dmg`
          : electronAssetName(candidate, platform, arch)
      if (
        release.assets.some(
          (asset: unknown) =>
            asset && typeof asset === 'object' && 'name' in asset && asset.name === expected,
        )
      )
        selected = candidate
    }
    if (releases.length < 100) break
    if (page === 10) throw new Error('The release list could not be checked completely.')
  }
  // Unsupported installations use the canonical release page, never an asset-supplied URL.
  const releaseUrl = `${updateRepository}/releases/tag/v${selected}`
  return selected === version ? null : { version: selected, releaseUrl, downloadUrl: releaseUrl }
}

import { createHash, randomUUID } from 'node:crypto'
import { createWriteStream } from 'node:fs'
import { mkdir, rename, rm } from 'node:fs/promises'
import type { RequestOptions } from 'node:http'
import { dirname, isAbsolute } from 'node:path'
import { Transform } from 'node:stream'
import { pipeline } from 'node:stream/promises'
import { CancellationToken } from 'builder-util-runtime'
import type { DownloadCallOptions } from 'builder-util-runtime/out/httpExecutor'
import type { UpdateRelease } from './application-updater'
import { UpdateHttpExecutor } from './update-http'
import { newerRelease, releaseVersion, updateRepository } from './update-policy'

export type DistributionKind = 'installed' | 'portable' | 'managed'
export interface DistributionRelease extends UpdateRelease {
  assetUrl: string
  size: number
  sha256: string
}
const maximumAssetSize = 4 * 1024 ** 3
const maximumMetadataSize = 4 * 1024 ** 2

function assetName(version: string, runtime: 'win-x64' | 'linux-x64', kind: DistributionKind) {
  if (!releaseVersion(version)) throw new Error('Invalid release version.')
  if (kind === 'installed' && runtime === 'win-x64') return `Winnow-${version}-win-x64-setup.exe`
  if (kind === 'managed' && runtime === 'linux-x64') return `Winnow-${version}-linux-x64.deb`
  if (kind === 'portable') return `Winnow-${version}-${runtime}.${runtime === 'win-x64' ? 'zip' : 'tar.gz'}`
  throw new Error('Unsupported distribution update.')
}

async function metadata(
  response: Response,
  budget: { bytes: number },
  signal: AbortSignal,
): Promise<unknown> {
  if (!response.ok || !response.body) throw new Error('The releases could not be read.')
  const reader = response.body.getReader()
  const abort = () => {
    void reader.cancel().catch(() => {})
  }
  signal.addEventListener('abort', abort, { once: true })
  const chunks: Uint8Array[] = []
  let complete = false
  try {
    for (;;) {
      signal.throwIfAborted()
      const part = await reader.read()
      if (part.done) break
      budget.bytes += part.value.byteLength
      if (budget.bytes > maximumMetadataSize) throw new Error('The release list is too large.')
      chunks.push(part.value)
    }
    signal.throwIfAborted()
    complete = true
    return JSON.parse(Buffer.concat(chunks).toString('utf8'))
  } finally {
    signal.removeEventListener('abort', abort)
    if (!complete) await reader.cancel().catch(() => {})
    reader.releaseLock()
  }
}

/** Select the newest eligible release before validating its artifact; never offer an older unverifiable fallback. */
export async function distributionRelease(options: {
  version: string
  includeBeta: boolean
  runtime: 'win-x64' | 'linux-x64'
  kind: DistributionKind
  signal?: AbortSignal
}): Promise<DistributionRelease | null> {
  const current = releaseVersion(options.version)
  if (!current || current.pre.some((value) => /^(dev|ci)$/i.test(value))) return null
  assetName(options.version, options.runtime, options.kind)
  const signal = AbortSignal.any([AbortSignal.timeout(30_000), ...(options.signal ? [options.signal] : [])])
  const budget = { bytes: 0 }
  let selected: { version: string; assets: unknown } | null = null
  for (let page = 1; page <= 10; page++) {
    signal.throwIfAborted()
    const response = await fetch(
      `https://api.github.com/repos/safwyls/winnow/releases?per_page=100&page=${page}`,
      {
        headers: { Accept: 'application/vnd.github+json' },
        redirect: 'error',
        signal,
      },
    )
    const entries = await metadata(response, budget, signal)
    if (!Array.isArray(entries) || entries.length > 100) throw new Error('Invalid release metadata.')
    for (const entry of entries) {
      if (!entry || typeof entry !== 'object') continue
      const release = entry as { tag_name?: unknown; draft?: unknown; prerelease?: unknown; assets?: unknown }
      if (
        release.draft !== false ||
        typeof release.prerelease !== 'boolean' ||
        (!options.includeBeta && release.prerelease) ||
        typeof release.tag_name !== 'string' ||
        !release.tag_name.startsWith('v')
      )
        continue
      const version = release.tag_name.slice(1)
      if (newerRelease(version, selected?.version ?? options.version, options.includeBeta))
        selected = { version, assets: release.assets }
    }
    if (entries.length < 100) break
    if (page === 10) throw new Error('The release list could not be checked completely.')
  }
  if (!selected) return null
  const name = assetName(selected.version, options.runtime, options.kind)
  if (!Array.isArray(selected.assets))
    throw new Error('The newest release has no verifiable distribution artifact.')
  const matches = selected.assets.filter((asset) => asset && typeof asset === 'object' && asset.name === name)
  if (matches.length !== 1) throw new Error('The newest release has no unique distribution artifact.')
  const asset = matches[0] as { browser_download_url?: unknown; size?: unknown; digest?: unknown }
  const assetUrl = `${updateRepository}/releases/download/v${selected.version}/${name}`
  if (
    asset.browser_download_url !== assetUrl ||
    typeof asset.size !== 'number' ||
    !Number.isSafeInteger(asset.size) ||
    asset.size <= 0 ||
    asset.size > maximumAssetSize ||
    typeof asset.digest !== 'string' ||
    !/^sha256:[a-f\d]{64}$/i.test(asset.digest)
  )
    throw new Error('The newest distribution artifact has an invalid URL, size or SHA-256 digest.')
  return {
    version: selected.version,
    releaseUrl: `${updateRepository}/releases/tag/v${selected.version}`,
    downloadUrl: assetUrl,
    assetUrl,
    size: asset.size,
    sha256: asset.digest.slice(7).toLowerCase(),
  }
}

class DistributionHttpExecutor extends UpdateHttpExecutor {
  transfer: Promise<void> | undefined
  private requests = 0
  constructor(
    private readonly release: DistributionRelease,
    private readonly signal: AbortSignal,
    private readonly progress: (percent: number) => void,
  ) {
    super()
  }

  override createRequest(options: RequestOptions, callback: (response: any) => void): Electron.ClientRequest {
    this.signal.throwIfAborted()
    if (++this.requests > 11) throw new Error('Too many update redirects.')
    const request = super.createRequest(options, callback)
    const abort = () => request.abort()
    this.signal.addEventListener('abort', abort, { once: true })
    request.once('close', () => this.signal.removeEventListener('abort', abort))
    if (this.signal.aborted) abort()
    return request
  }

  protected override doDownload(options: RequestOptions, call: DownloadCallOptions, redirects: number) {
    try {
      super.doDownload(
        options,
        {
          ...call,
          responseHandler: (response, done) => {
            if (this.signal.aborted || response.statusCode !== 200) {
              response.destroy()
              done(new Error('The update could not be downloaded.'))
              return
            }
            let bytes = 0
            const hash = createHash('sha256'),
              expected = this.release
            const progress = this.progress
            const guard = new Transform({
              transform(chunk: Buffer, _encoding, next) {
                bytes += chunk.length
                if (bytes > expected.size) {
                  next(new Error('The update exceeded its declared size.'))
                  return
                }
                hash.update(chunk)
                try {
                  progress(Math.min(99, (bytes / expected.size) * 100))
                } catch (error) {
                  next(error as Error)
                  return
                }
                next(null, chunk)
              },
              flush(next) {
                next(
                  bytes !== expected.size || hash.digest('hex') !== expected.sha256
                    ? new Error('The update failed size or SHA-256 verification.')
                    : null,
                )
              },
            })
            this.transfer = pipeline(response, guard, createWriteStream(call.destination!, { flags: 'wx' }), {
              signal: this.signal,
            })
            void this.transfer.then(() => done(null), done)
          },
        },
        redirects,
      )
    } catch (error) {
      call.callback(error as Error)
    }
  }
}

/** Only fully verified bytes become a staged artifact; failed or cancelled attempts leave retry safe. */
export async function downloadDistribution(
  release: DistributionRelease,
  destination: string,
  signal: AbortSignal,
  progress: (percent: number) => void,
): Promise<void> {
  signal.throwIfAborted()
  const suffixes = [
    assetName(release.version, 'win-x64', 'installed'),
    assetName(release.version, 'win-x64', 'portable'),
    assetName(release.version, 'linux-x64', 'portable'),
    assetName(release.version, 'linux-x64', 'managed'),
  ]
  if (
    !isAbsolute(destination) ||
    !Number.isSafeInteger(release.size) ||
    release.size <= 0 ||
    release.size > maximumAssetSize ||
    !/^[a-f\d]{64}$/.test(release.sha256) ||
    !suffixes.some(
      (name) => release.assetUrl === `${updateRepository}/releases/download/v${release.version}/${name}`,
    ) ||
    release.downloadUrl !== release.assetUrl ||
    release.releaseUrl !== `${updateRepository}/releases/tag/v${release.version}`
  )
    throw new Error('Invalid distribution download metadata.')
  await mkdir(dirname(destination), { recursive: true })
  const partial = `${destination}.${randomUUID()}.partial`
  const executor = new DistributionHttpExecutor(release, signal, progress)
  const token = new CancellationToken(),
    cancel = () => token.cancel()
  signal.addEventListener('abort', cancel, { once: true })
  if (signal.aborted) cancel()
  try {
    await executor.download(new URL(release.assetUrl), partial, { cancellationToken: token })
    signal.throwIfAborted()
    await rename(partial, destination)
    progress(100)
  } finally {
    signal.removeEventListener('abort', cancel)
    token.dispose()
    await executor.transfer?.catch(() => {})
    await rm(partial, { force: true })
  }
}

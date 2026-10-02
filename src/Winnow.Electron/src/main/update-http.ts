import type { RequestOptions } from 'node:http'
import { CancellationToken } from 'builder-util-runtime'
import type { DownloadCallOptions } from 'builder-util-runtime/out/httpExecutor'
import { ElectronHttpExecutor } from 'electron-updater/out/electronHttpExecutor.js'

function validateLocation(options: RequestOptions) {
  const hostname = options.hostname ?? options.host
  if (
    options.protocol !== 'https:' ||
    options.auth ||
    (options.port && String(options.port) !== '443') ||
    ![
      'github.com',
      'api.github.com',
      'release-assets.githubusercontent.com',
      'objects.githubusercontent.com',
    ].includes(String(hostname))
  )
    throw new Error('The update request left GitHub release storage.')
}

/** Metadata cancellation and redirect policy cover the native updater's requests as well as its initial URLs. */
export class UpdateHttpExecutor extends ElectronHttpExecutor {
  private checkSignal?: AbortSignal
  async duringCheck<T>(signal: AbortSignal | undefined, check: () => Promise<T>): Promise<T> {
    this.checkSignal = signal
    try {
      return await check()
    } finally {
      this.checkSignal = undefined
    }
  }
  override async request(
    options: RequestOptions,
    parent?: CancellationToken,
    data?: { [name: string]: unknown } | null,
  ) {
    validateLocation(options)
    const token = new CancellationToken(parent),
      signal = this.checkSignal,
      cancel = () => token.cancel()
    signal?.addEventListener('abort', cancel, { once: true })
    if (signal?.aborted) cancel()
    try {
      return await super.request(options, token, data)
    } finally {
      signal?.removeEventListener('abort', cancel)
      token.dispose()
    }
  }
  override createRequest(options: RequestOptions, callback: (response: any) => void): Electron.ClientRequest {
    validateLocation(options)
    return super.createRequest(options, callback)
  }
  protected override doDownload(options: RequestOptions, call: DownloadCallOptions, redirects: number) {
    try {
      validateLocation(options)
    } catch (error) {
      call.callback(error as Error)
      return
    }
    super.doDownload(options, call, redirects)
  }
}

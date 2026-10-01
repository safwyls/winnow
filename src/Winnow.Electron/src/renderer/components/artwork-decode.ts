import type { OwnedArtwork } from './artwork-cache'

type DecodeJob = {
  source?: string
  signal: AbortSignal
  reload(): Promise<string | undefined>
  publish(source: string): void
  complete(value: OwnedArtwork | null): void
  fail(error: unknown): void
  cancel(): void
}

/** Downloads do not hold a browser conversion permit; queued encoded strings have a separate budget. */
export class ArtworkDecoder {
  private readonly concurrent: number
  private readonly maxQueuedBytes: number
  private queue: DecodeJob[] = []
  private running = 0
  private bytes = 0
  private closed = false

  constructor(options: { concurrent?: number; maxQueuedBytes?: number } = {}) {
    this.concurrent = Math.max(1, options.concurrent ?? 6)
    this.maxQueuedBytes = Math.max(0, options.maxQueuedBytes ?? 32 * 1024 * 1024)
  }

  get queuedBytes() {
    return this.bytes
  }
  get queuedCount() {
    return this.queue.length
  }
  get activeCount() {
    return this.running
  }

  decode(source: string, signal: AbortSignal, reload: DecodeJob['reload'], publish: DecodeJob['publish']) {
    if (this.closed || signal.aborted) return Promise.resolve(null)
    return new Promise<OwnedArtwork | null>((complete, fail) => {
      // Under pressure, retain only the request identity. Re-read the backend's now-cached bytes
      // once a conversion permit is available, so an attached image eventually completes.
      const keep = this.running < this.concurrent || this.bytes + source.length * 2 <= this.maxQueuedBytes
      const job: DecodeJob = {
        source: keep ? source : undefined,
        signal,
        reload,
        publish,
        complete,
        fail,
        cancel: () => {
          const index = this.queue.indexOf(job)
          if (index < 0) return
          this.queue.splice(index, 1)
          this.bytes -= (job.source?.length ?? 0) * 2
          job.source = undefined
          signal.removeEventListener('abort', job.cancel)
          complete(null)
        },
      }
      this.bytes += (job.source?.length ?? 0) * 2
      this.queue.push(job)
      signal.addEventListener('abort', job.cancel, { once: true })
      this.drain()
    })
  }

  close() {
    this.closed = true
    for (const job of [...this.queue]) job.cancel()
  }

  private drain() {
    while (!this.closed && this.running < this.concurrent && this.queue.length) {
      const job = this.queue.shift()!
      this.bytes -= (job.source?.length ?? 0) * 2
      job.signal.removeEventListener('abort', job.cancel)
      this.running++
      void this.run(job)
    }
  }

  private async run(job: DecodeJob) {
    let pixels: OwnedArtwork | null = null
    let failed = false
    let failure: unknown
    try {
      job.signal.throwIfAborted()
      const source = job.source ?? (await job.reload())
      job.source = undefined
      job.signal.throwIfAborted()
      if (source) {
        pixels = await decodeArtworkImage(source, job.signal)
        if (pixels && (this.closed || job.signal.aborted)) {
          pixels.dispose()
          pixels = null
        } else if (pixels) job.publish(source)
      }
    } catch (error) {
      pixels?.dispose()
      pixels = null
      failed = !job.signal.aborted && !this.closed
      failure = error
    } finally {
      if (failed) job.fail(failure)
      else job.complete(pixels)
      this.running--
      this.drain()
    }
  }
}

export async function decodeArtworkImage(source: string, signal: AbortSignal): Promise<OwnedArtwork | null> {
  signal.throwIfAborted()
  if (!source.startsWith('data:image/png;base64,')) return null
  const bytes = Uint8Array.from(atob(source.slice('data:image/png;base64,'.length)), (character) =>
    character.charCodeAt(0),
  )
  const url = URL.createObjectURL(new Blob([bytes], { type: 'image/png' }))
  const image = new Image()
  let released = false
  const dispose = () => {
    if (released) return
    released = true
    image.src = ''
    URL.revokeObjectURL(url)
  }
  signal.addEventListener('abort', dispose, { once: true })
  try {
    image.src = url
    await image.decode()
    signal.throwIfAborted()
    if (!image.naturalWidth || !image.naturalHeight) {
      dispose()
      return null
    }
    return { source: url, width: image.naturalWidth, height: image.naturalHeight, dispose }
  } catch {
    dispose()
    return null
  } finally {
    signal.removeEventListener('abort', dispose)
  }
}

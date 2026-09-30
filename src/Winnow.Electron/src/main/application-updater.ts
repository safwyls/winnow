import type { ApplicationUpdateAction, ApplicationUpdateSnapshot } from '../shared/bridge'

export interface UpdateRelease {
  version: string
  releaseUrl: string
  downloadUrl: string
}
export interface UpdateDriver {
  supported: boolean
  check(includeBeta: boolean, signal?: AbortSignal): Promise<UpdateRelease | null>
  download(release: UpdateRelease, signal: AbortSignal, progress: (percent: number) => void): Promise<void>
  install(): Promise<void>
  discard(): Promise<void>
}
export interface UpdatePorts {
  version: string
  packaged: boolean
  driver: UpdateDriver
  preferences(): Promise<{ automatic: boolean; includeBeta: boolean }>
  savePreference(name: 'AutomaticUpdates' | 'IncludeBetaUpdates', value: boolean): Promise<void>
  stopBackend(): Promise<void>
  recoverBackend(): Promise<void>
  prepareRestart(version: string): Promise<void>
  clearRestart(): Promise<void>
  openDownload(url: string): Promise<void>
}
export const initialUpdateSnapshot = (): ApplicationUpdateSnapshot => ({
  automatic: true,
  includeBeta: false,
  busy: false,
  canDownload: false,
  canRestart: false,
  canCancel: false,
  progress: 0,
  status: 'Updates have not been checked yet.',
})

/** One updater state feeds settings and both presentation headers. */
export class ApplicationUpdater {
  private value = initialUpdateSnapshot()
  private release: UpdateRelease | null = null
  private running: Promise<void> | null = null
  private downloadCancellation: AbortController | null = null
  private checkCancellation: AbortController | null = null
  private generation = 0
  private loaded = false
  private disposed = false
  private handedOff = false
  private timers: ReturnType<typeof setTimeout>[] = []
  private listeners = new Set<(snapshot: ApplicationUpdateSnapshot) => void>()
  constructor(private readonly ports: UpdatePorts) {}
  get snapshot(): ApplicationUpdateSnapshot {
    return { ...this.value }
  }
  subscribe(listener: (snapshot: ApplicationUpdateSnapshot) => void) {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }
  private publish(patch: Partial<ApplicationUpdateSnapshot>) {
    if (this.disposed) return
    this.value = { ...this.value, ...patch }
    for (const listener of this.listeners) listener(this.snapshot)
  }
  private async load() {
    if (!this.loaded) {
      const preferences = await this.ports.preferences()
      this.loaded = true
      this.publish(preferences)
    }
  }
  async initialize() {
    try {
      await this.load()
    } catch {
      this.publish({ status: 'Update preferences could not be read. Try checking manually.' })
    }
    if (!this.ports.packaged || this.disposed) return
    const repeat = async () => {
      if (this.value.automatic) await this.check()
      if (!this.disposed) this.timers.push(setTimeout(() => void repeat(), 6 * 60 * 60_000))
    }
    this.timers.push(setTimeout(() => void repeat(), 20_000))
  }
  dispose() {
    this.disposed = true
    this.generation++
    this.downloadCancellation?.abort()
    this.checkCancellation?.abort()
    this.timers.forEach(clearTimeout)
    this.listeners.clear()
    return this.running ?? Promise.resolve()
  }
  private run(work: () => Promise<void>): Promise<void> {
    if (this.running || this.disposed || this.handedOff) return this.running ?? Promise.resolve()
    this.running = work()
      .catch(() => {
        this.publish({ status: 'Could not complete the update action. Try again.' })
      })
      .finally(() => {
        this.running = null
        this.publish({ busy: this.handedOff, canCancel: false })
      })
    return this.running
  }
  async check(): Promise<void> {
    const generation = this.generation
    await this.run(async () => {
      await this.load()
      if (this.disposed || generation !== this.generation) return
      if (!this.ports.packaged || /-(?:dev|ci)(?:\.|$)/i.test(this.ports.version)) {
        this.publish({ status: 'Development and CI builds do not receive release updates.' })
        return
      }
      this.publish({ busy: true, status: 'Checking for updates…' })
      let release: UpdateRelease | null
      const cancellation = new AbortController()
      this.checkCancellation = cancellation
      try {
        release = await this.ports.driver.check(this.value.includeBeta, cancellation.signal)
      } catch (failure) {
        this.release = null
        this.publish({
          canDownload: false,
          canRestart: false,
          availableVersion: null,
          releaseUrl: null,
          downloadUrl: null,
        })
        throw failure
      } finally {
        this.checkCancellation = null
      }
      if (generation !== this.generation || this.disposed) return
      if (release?.version !== this.release?.version) this.publish({ canRestart: false, progress: 0 })
      this.release = release
      this.publish(
        release
          ? {
              availableVersion: release.version,
              releaseUrl: release.releaseUrl,
              downloadUrl: release.downloadUrl,
              canDownload: this.ports.driver.supported && !this.value.canRestart,
              status: this.value.canRestart
                ? 'Update ready. Restart when you are ready.'
                : this.ports.driver.supported
                  ? 'A new version is available.'
                  : 'A new version is available. Download it in your browser to update this installation.',
            }
          : {
              canDownload: false,
              canRestart: false,
              availableVersion: null,
              releaseUrl: null,
              downloadUrl: null,
              status: 'Winnow is up to date for this channel.',
            },
      )
    })
    if (generation === this.generation && this.value.automatic && this.value.canDownload)
      await this.download()
  }
  download(): Promise<void> {
    return this.run(async () => {
      const release = this.release
      if (!release || !this.ports.driver.supported || !this.value.canDownload || this.value.canRestart) return
      const cancellation = new AbortController(),
        generation = this.generation
      this.downloadCancellation = cancellation
      this.publish({ busy: true, canCancel: true, progress: 0, status: 'Downloading and verifying update…' })
      try {
        await this.ports.driver.download(release, cancellation.signal, (percent) => {
          if (generation === this.generation) this.publish({ progress: Math.min(100, Math.max(0, percent)) })
        })
        if (cancellation.signal.aborted || generation !== this.generation) {
          this.publish({ status: 'Update download cancelled.' })
          return
        }
        this.publish({
          canDownload: false,
          canRestart: true,
          progress: 100,
          status: 'Update ready. Restart when you are ready.',
        })
      } catch {
        this.publish({
          canRestart: false,
          status: cancellation.signal.aborted
            ? 'Update download cancelled.'
            : 'The update could not be downloaded or verified. Try again.',
        })
      } finally {
        this.downloadCancellation = null
      }
    })
  }
  cancel() {
    this.downloadCancellation?.abort()
  }
  restart(): Promise<void> {
    return this.run(async () => {
      if (!this.release || !this.value.canRestart || !this.ports.driver.supported) return
      let stopping = false
      this.publish({ busy: true, status: 'Stopping the library service and preparing the update…' })
      try {
        await this.ports.stopBackend()
        stopping = true
        await this.ports.prepareRestart(this.release.version)
        await this.ports.driver.install()
        this.handedOff = true
        this.publish({ canRestart: false, canDownload: false, status: 'The update installer has started.' })
      } catch {
        await this.ports.driver.discard().catch(() => {})
        await this.ports.clearRestart().catch(() => {})
        this.publish({
          canRestart: false,
          canDownload: true,
          status: 'The update could not start. Try downloading it again.',
          recoveryStatus:
            'The previous update did not start. Your library is unchanged. Download the update again when you are ready.',
        })
        if (stopping) {
          try {
            await this.ports.recoverBackend()
          } catch {
            this.publish({
              status:
                'The update did not start and the library service did not reconnect. Close and reopen Winnow.',
            })
          }
        }
      }
    })
  }
  async updateAndRestart() {
    if (this.value.busy) return
    if (this.value.canDownload) await this.download()
    if (this.value.canRestart && !this.value.busy) await this.restart()
  }
  async setPreference(name: 'AutomaticUpdates' | 'IncludeBetaUpdates', value: boolean) {
    if (this.disposed || this.handedOff || (this.value.busy && this.value.canRestart)) return
    this.generation++
    this.cancel()
    this.checkCancellation?.abort()
    await this.running
    if (this.disposed) return
    await this.load()
    await this.ports.savePreference(name, value)
    if (name === 'IncludeBetaUpdates') {
      this.release = null
      await this.ports.driver.discard()
      this.publish({
        includeBeta: value,
        canDownload: false,
        canRestart: false,
        availableVersion: null,
        releaseUrl: null,
        downloadUrl: null,
        progress: 0,
        status: 'Update channel changed.',
      })
    } else this.publish({ automatic: value })
    if (this.value.automatic) void this.check()
  }
  async refreshPreferences() {
    if (this.disposed || this.handedOff || (this.value.busy && this.value.canRestart)) return
    const latest = await this.ports.preferences()
    if (latest.automatic === this.value.automatic && latest.includeBeta === this.value.includeBeta) return
    this.generation++
    this.cancel()
    this.checkCancellation?.abort()
    await this.running
    if (this.disposed) return
    const preferences = await this.ports.preferences()
    if (preferences.includeBeta !== this.value.includeBeta) {
      this.release = null
      await this.ports.driver.discard()
      this.publish({
        canDownload: false,
        canRestart: false,
        availableVersion: null,
        releaseUrl: null,
        downloadUrl: null,
        progress: 0,
        status: 'Update channel changed.',
      })
    }
    this.loaded = true
    this.publish(preferences)
  }
  async action(action: ApplicationUpdateAction, value?: boolean): Promise<ApplicationUpdateSnapshot> {
    if (action === 'check') await this.check()
    else if (action === 'download') await this.download()
    else if (action === 'restart') await this.restart()
    else if (action === 'update-and-restart') await this.updateAndRestart()
    else if (action === 'cancel') this.cancel()
    else if (action === 'automatic' || action === 'beta') {
      if (typeof value !== 'boolean') throw new Error('An update preference value is required.')
      await this.setPreference(action === 'automatic' ? 'AutomaticUpdates' : 'IncludeBetaUpdates', value)
    } else if (action === 'manual-download') {
      if (this.release) await this.ports.openDownload(this.release.downloadUrl)
    } else if (action === 'release-notes') {
      if (this.release) await this.ports.openDownload(this.release.releaseUrl)
    } else throw new Error('Unknown update action.')
    return this.snapshot
  }
}

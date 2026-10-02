/** Coalesce repeated invalidations without losing changes that arrive during an older read. */
export class SnapshotRefresh {
  private dirty = false
  private pending: Promise<void> | undefined
  constructor(private readonly read: () => Promise<void>) {}
  request(): Promise<void> {
    this.dirty = true
    if (!this.pending)
      this.pending = this.drain().finally(() => {
        this.pending = undefined
      })
    return this.pending
  }
  private async drain(): Promise<void> {
    while (this.dirty) {
      this.dirty = false
      await this.read()
    }
  }
}

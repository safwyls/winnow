import type { WebContents } from 'electron'
import type { BrowserKey } from './browser-controller'

/** Locked input is discarded before readiness, and again if its document changes while waiting. */
export class AccountInputGate {
  private enabled = true
  private generation = 0
  constructor(
    private readonly contents: Pick<WebContents, 'focus' | 'sendInputEvent' | 'insertText' | 'isDestroyed'>,
    private readonly ready: () => Promise<void>,
  ) {}
  setEnabled(enabled: boolean) {
    this.enabled = enabled
    this.generation++
  }
  navigated() {
    this.generation++
  }
  private accepts(generation: number) {
    return this.enabled && generation === this.generation && !this.contents.isDestroyed()
  }
  async key(key: BrowserKey) {
    const generation = this.generation
    if (!this.accepts(generation)) return
    await this.ready()
    if (!this.accepts(generation)) return
    this.contents.focus()
    const modifiers = key.shift ? ['shift' as const] : []
    this.contents.sendInputEvent({ type: 'keyDown', keyCode: key.key, modifiers })
    if (key.key === 'Enter' || key.key === 'Space')
      this.contents.sendInputEvent({ type: 'char', keyCode: key.key === 'Enter' ? '\r' : ' ', modifiers })
    this.contents.sendInputEvent({ type: 'keyUp', keyCode: key.key, modifiers })
  }
  async text(text: string) {
    const generation = this.generation
    if (!text || text.length > 4096 || !this.accepts(generation)) return
    await this.ready()
    if (!this.accepts(generation)) return
    this.contents.focus()
    await this.contents.insertText(text)
  }
}

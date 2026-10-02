interface TrayWindow {
  show(): void
  isVisible(): boolean
  hide(): void
  focus(): void
  isMinimized(): boolean
  restore(): void
  setSkipTaskbar(skip: boolean): void
  setFullScreen(fullscreen: boolean): void
}

interface TrayIcon {
  destroy(): void
}

export function savedBoolean(value: string | null | undefined): boolean {
  return value?.trim().toLowerCase() === 'true'
}

/** A hidden window must keep its recovery icon even after both tray preferences are disabled. */
export class WindowTrayController {
  private icon: TrayIcon | undefined
  private hidden = false
  private background: boolean
  private stopped = false
  private startupApplied = false
  private minimizeToTray = false
  private closeToTray = false

  constructor(
    private readonly options: {
      background: boolean
      window(): TrayWindow | undefined
      createIcon(): TrayIcon
    },
  ) {
    this.background = options.background
  }

  prepare(): void {
    this.refreshIcon()
    if (this.background && this.icon) this.options.window()?.setSkipTaskbar(true)
  }

  ready(): void {
    if (!this.stopped && (!this.background || !this.icon)) this.show()
  }

  preferences(values: Record<string, string | null>): void {
    if (this.stopped) return
    this.minimizeToTray = savedBoolean(values.MinimizeToTray)
    this.closeToTray = savedBoolean(values.CloseToTray)
    this.refreshIcon()
    const window = this.options.window()
    if (!this.startupApplied && window) {
      this.startupApplied = true
      // A background launch stays in desktop mode, including an early activation before this read.
      if (!this.options.background && savedBoolean(values.StartInFullscreen)) window.setFullScreen(true)
    }
  }

  shown(): void {
    this.background = false
    this.hidden = false
    this.options.window()?.setSkipTaskbar(false)
    this.refreshIcon()
  }

  minimized(): void {
    if (this.minimizeToTray) this.hide()
  }

  closing(): boolean {
    return this.closeToTray && this.hide()
  }

  restore(): void {
    if (this.stopped) return
    const window = this.options.window()
    if (!window) return
    window.setSkipTaskbar(false)
    // Calling restore on an already maximized window would change its presentation.
    if (window.isMinimized()) window.restore()
    this.show()
    window.focus()
    this.shown()
  }

  dispose(): void {
    this.stopped = true
    this.icon?.destroy()
    this.icon = undefined
  }

  private show(): void {
    const window = this.options.window()
    if (!window) return
    window.show()
    // Released Windows updaters used SW_HIDE. Windows consumes that startup flag
    // on the first ShowWindow call, so an explicitly requested window needs a second call.
    if (!window.isVisible()) window.show()
  }

  private hide(): boolean {
    if (this.stopped) return false
    this.refreshIcon()
    const window = this.options.window()
    if (!this.icon || !window) return false
    this.hidden = true
    window.setSkipTaskbar(true)
    window.hide()
    return true
  }

  private refreshIcon(): void {
    if (this.stopped) return
    const wanted = this.background || this.hidden || this.minimizeToTray || this.closeToTray
    if (!wanted) {
      this.icon?.destroy()
      this.icon = undefined
    } else if (!this.icon) {
      try {
        this.icon = this.options.createIcon()
      } catch {
        // A desktop without a notification area must retain an ordinary visible window.
      }
    }
  }
}

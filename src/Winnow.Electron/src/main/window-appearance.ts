import { parseWindowAppearance, type WindowAppearanceResult } from '../shared/windowAppearance'

interface AppearanceWindow {
  isDestroyed(): boolean
  setBackgroundMaterial(value: 'acrylic' | 'mica' | 'none'): void
  setBackgroundColor(value: string): void
}
export interface AppearanceEnvironment {
  platform: string
  release: string
  highContrast: boolean
  reducedTransparency: boolean
  remoteSession: boolean
}

export function supportsWindowMaterial(environment: AppearanceEnvironment): boolean {
  const [major, , build] = environment.release.split('.').map(Number)
  return environment.platform === 'win32' && major >= 10 && build >= 22621 &&
    !environment.highContrast && !environment.reducedTransparency && !environment.remoteSession
}

/** Never changes whole-window opacity: only the backing material is requested here. */
export class WindowAppearanceController {
  constructor(private readonly window: AppearanceWindow, private readonly environment: () => AppearanceEnvironment) {}
  apply(value: unknown): WindowAppearanceResult {
    const request = parseWindowAppearance(value), environment = this.environment()
    const supported = supportsWindowMaterial(environment)
    if (this.window.isDestroyed()) return { requested: 'none', supported: false, platform: environment.platform }
    try {
      if (supported && request.enabled) {
        this.window.setBackgroundMaterial(request.material)
        this.window.setBackgroundColor('#00000000')
        return { requested: request.material, supported, platform: environment.platform }
      }
      if (environment.platform === 'win32') this.window.setBackgroundMaterial('none')
      this.window.setBackgroundColor(request.background)
      return { requested: 'none', supported, platform: environment.platform }
    } catch {
      try { if (environment.platform === 'win32') this.window.setBackgroundMaterial('none') } catch { /* Restore the opaque client even when DWM refuses the material. */ }
      this.window.setBackgroundColor(request.background)
      return { requested: 'none', supported: false, platform: environment.platform }
    }
  }
}

import type { ApiRequest, ApiResult } from '../shared/bridge'
import type { AppearanceSession } from '../shared/appearance-session'
import { serializeProfile } from './storage'

/** Capture edits remain live but never reach either appearance preference store. */
export class SessionAppearance {
  private profile: unknown = null
  private readonly values: Map<string, string | null>
  constructor(readonly options: AppearanceSession) {
    this.values = new Map([
      ['Theme', options.palette],
      ['Typography', null],
      ['Transparency', String(options.transparency)],
      ['Backdrop', options.backdrop],
      ['TranslucentWall', String(options.wallTranslucent)],
      ['Layout', options.layout],
    ])
  }
  loadProfile(): unknown {
    return structuredClone(this.profile)
  }
  saveProfile(value: unknown): void {
    this.profile = JSON.parse(serializeProfile(value))
  }
  async request(input: ApiRequest, next: () => Promise<ApiResult>): Promise<ApiResult> {
    if (input.route === 'preferences.presentation.put' && this.values.has(String(input.params?.preference))) {
      const value =
        input.body && typeof input.body === 'object' && 'value' in input.body ? input.body.value : undefined
      if (typeof value !== 'string' && value !== null)
        return { ok: false, status: 400, message: 'A preference value is required.' }
      this.values.set(String(input.params!.preference), value)
      return { ok: true, status: 204 }
    }
    const result = await next()
    if (input.route !== 'preferences.presentation.get' || !result.ok || !Array.isArray(result.data))
      return result
    return {
      ...result,
      data: [
        ...result.data.filter((row) => !this.values.has(row.preference)),
        ...[...this.values].map(([preference, value]) => ({ preference, value })),
      ],
    }
  }
}

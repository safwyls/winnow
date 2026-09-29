export interface WindowAppearanceRequest {
  enabled: boolean
  material: 'acrylic' | 'mica'
  background: string
}
export interface WindowAppearanceResult {
  /** Electron confirms the request, but does not expose the compositor's active material. */
  requested: 'acrylic' | 'mica' | 'none'
  supported: boolean
  platform: string
}
export function parseWindowAppearance(value: unknown): WindowAppearanceRequest {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid window appearance.')
  const request = value as WindowAppearanceRequest
  if (Object.keys(request).some((key) => !['enabled', 'material', 'background'].includes(key)) ||
    typeof request.enabled !== 'boolean' || !['acrylic', 'mica'].includes(request.material) ||
    typeof request.background !== 'string' || !/^#[0-9a-f]{6}$/i.test(request.background)) throw new Error('Invalid window appearance.')
  return request
}

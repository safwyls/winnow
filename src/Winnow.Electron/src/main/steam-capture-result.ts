import type { SteamCaptureResult } from '../shared/bridge'

export function steamCaptureFailure(
  captureOutcome: 'cancelled' | 'unavailable' | 'failed' | 'no-session',
  captureDetail: string,
): SteamCaptureResult {
  return { captureOutcome, captureDetail, licensesPagesWalked: 0, loadMoreClicks: 0 }
}

export interface EpicSignInPreparation {
  attemptId: string
  expiresAt: string
  consentNotice: string
}
export interface EpicSignInOptions {
  attemptId: string
  consentGranted: boolean
}
export interface EpicSignInResult {
  succeeded: boolean
  failure: number
  persisted: boolean
  accountId?: string | null
  displayName?: string | null
  canRetryManually?: boolean
  captureRoute?: 'launcher bridge' | 'redirect' | 'JSON body' | 'session harvest' | 'manual'
}
export interface EpicPromptRequest {
  providerName: string
  startUrl: string
  consentNotice: string
  redirectUrl?: string | null
  redirectCodeParameter: string
  expectedState?: string | null
  stateParameter: string
  additionalNavigableOrigins: string[]
  harvestUrl?: string | null
  jsonCodeFields: { fieldName: string; kind: 0 | 1 }[]
  strategies: number
  profileKey: string
  timeout: string
}
export interface EpicAuthChallenge {
  attemptId: string
  expiresAt: string
  request: EpicPromptRequest
}

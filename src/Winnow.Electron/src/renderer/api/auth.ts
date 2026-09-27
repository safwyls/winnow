export interface EpicChallenge {
  attemptId: string
  expiresAt: string
  request: {
    startUrl: string
    consentNotice: string
    redirectUrl?: string | null
    redirectCodeParameter: string
    expectedState?: string | null
    stateParameter: string
  }
}

/** A pasted callback must identify this exact challenge before its one-use code is submitted. */
export function readEpicCallback(
  input: string,
  challenge: EpicChallenge,
): { code: string; kind: number; state?: string | null } {
  if (new Date(challenge.expiresAt).getTime() <= Date.now())
    throw new Error('This sign-in expired. Start again for a new link.')
  const value = input.trim()
  const prompt = challenge.request
  if (prompt.expectedState || /^https?:\/\//i.test(value)) {
    let callback: URL
    let expected: URL
    try {
      callback = new URL(value)
      expected = new URL(prompt.redirectUrl ?? '')
    } catch {
      throw new Error('Paste the complete address from the final sign-in page.')
    }
    if (
      callback.origin !== expected.origin ||
      callback.pathname !== expected.pathname ||
      callback.username ||
      callback.password
    )
      throw new Error('This address is not the callback for this sign-in.')
    const state = callback.searchParams.get(prompt.stateParameter || 'state')
    if (prompt.expectedState && state !== prompt.expectedState)
      throw new Error('This address belongs to a different sign-in. Start again.')
    const code = callback.searchParams.get(prompt.redirectCodeParameter || 'code')
    if (!code || code.length > 4096 || /[\x00-\x1f]/.test(code))
      throw new Error('The callback did not contain a valid authorization code.')
    return { code, kind: 0, state }
  }
  if (!value || value.length > 4096 || /[\x00-\x1f]/.test(value))
    throw new Error('Enter the authorization code from the sign-in page.')
  return { code: value, kind: 0, state: null }
}

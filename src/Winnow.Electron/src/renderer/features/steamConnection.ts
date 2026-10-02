import type { StoreConnections } from '../api/types'

export const steamCapturePermissionExplanation =
  'Off by default. Declining is a complete answer: purchase history and licence pages are never opened unless you choose to capture them. Capture reads what you bought, what you paid and how licences arrived. You review the captured pages before importing.'
export const steamHealthMessages = [
  'No sign-in session is stored.',
  'The sign-in is working.',
  'Renews automatically.',
  'Renewal was attempted and did not succeed. Signing in again will restore it. An API key, if set, keeps scheduled updates running regardless.',
  'The sign-in has expired and cannot be used. Only a fresh sign-in can recover it. An API key, if set, is unaffected.',
  'The sign-in is working for this session, but this machine cannot encrypt it, so Winnow did not save it to disk. You will need to sign in again after a restart.',
] as const
export function steamConnectionState(snapshot: StoreConnections) {
  const steam = snapshot.steam,
    health = snapshot.steamHealth ?? (steam.sessionUsable ? 1 : steam.hasSession ? 4 : 0)
  const live = health === 1 || health === 5,
    renew = health === 2 || health === 3
  return {
    health,
    label: renew
      ? 'SIGN-IN NEEDS RENEWING'
      : health === 4
        ? 'SIGN-IN EXPIRED'
        : live
          ? steam.hasApiKey
            ? 'SIGNED IN, KEY SET'
            : 'SIGNED IN'
          : steam.hasApiKey
            ? 'KEY SET'
            : 'NO CONNECTION',
    live: live || (health === 0 && steam.hasApiKey),
    attention: health === 3 || health === 4,
    healthAttention: health === 3 || health === 4 || health === 5,
    healthMessage: steamHealthMessages[health] ?? steamHealthMessages[0],
    terse:
      [
        'Not signed in',
        'Working',
        'Renewing automatically',
        'Renewal failing',
        'Expired',
        'Working, not saved',
      ][health] ?? 'Unknown session state',
    showSignIn: !live,
    signInLabel: steam.hasSession ? 'Sign in again' : 'Sign in to Steam',
    keyState: !steam.hasApiKey
      ? 'Not set'
      : steam.apiKeyIsAppManaged
        ? 'Set'
        : "Set outside Winnow, can't be cleared here",
    keyInUse: steam.hasApiKey,
    signInInUse: !steam.hasApiKey && steam.sessionUsable,
    connectionMessage:
      steam.hasApiKey || steam.hasSession
        ? 'Adds games never installed on this PC.'
        : 'Games never touched on this PC are not in your library yet.',
  }
}

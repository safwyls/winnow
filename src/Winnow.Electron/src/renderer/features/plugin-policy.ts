export function pluginWebUrl(value?: string | null): string | null {
  if (!value || /[\u0000-\u001f\u007f]/.test(value)) return null
  try {
    const url = new URL(value)
    return url.protocol === 'https:' && !url.username && !url.password ? url.href : null
  } catch {
    return null
  }
}

export const pluginInstallationNote =
  "Install official plugins from the Winnow website, or place a plugin ZIP or unpacked plugin in the plugins folder, then choose Restart library service. ZIPs unpack automatically. Manually added plugins need enabling and another service restart. Connected windows reconnect automatically. Only enable plugins from authors you trust: plugins run with Winnow's access to this device."
export const pluginSecretNote =
  'Secrets are stored securely on this device and are never shown again. Leave a secret blank to keep its saved value.'

export interface PluginChallenge {
  attemptId: string
  verificationUrl: string
  userCode: string
  expiresAt: string
  pollIntervalSeconds: number
}
export function validPluginChallenge(
  challenge: PluginChallenge | null,
  hosts: readonly string[],
  now = Date.now(),
): challenge is PluginChallenge {
  if (!challenge) return false
  const destination = pluginWebUrl(challenge.verificationUrl)
  const expiry = Date.parse(challenge.expiresAt)
  return (
    challenge.attemptId.length > 0 &&
    challenge.attemptId.length <= 256 &&
    !/[\u0000-\u001f\u007f]/.test(challenge.attemptId) &&
    challenge.userCode.length <= 64 &&
    /[a-z0-9]/i.test(challenge.userCode) &&
    /^[a-z0-9 -]+$/i.test(challenge.userCode) &&
    challenge.pollIntervalSeconds >= 1 &&
    challenge.pollIntervalSeconds <= 60 &&
    Number.isFinite(expiry) &&
    expiry > now &&
    expiry <= now + 3600000 &&
    challenge.verificationUrl.length <= 2048 &&
    !!destination &&
    !new URL(destination).port &&
    hosts.some((host) => host.toLowerCase() === new URL(destination).hostname.toLowerCase())
  )
}

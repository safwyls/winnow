import type { ApplicationActivation } from '../shared/bridge'

/** URI launch syntax is checked before options can select or create a data directory. */
export function validateActivationArguments(args: string[]): void {
  const hasUri = args.some(
    (value) => value === '--uri' || /^winnow:/i.test(value) || value.startsWith('--uri='),
  )
  if (hasUri && readActivation(args).kind !== 'plugin')
    throw new Error('The Winnow installation link or its startup arguments are invalid.')
}

/** Electron's structured second-instance payload is still an input boundary. */
export function validatedActivation(value: unknown): ApplicationActivation {
  if (!value || typeof value !== 'object') return { kind: 'show' }
  const input = value as Record<string, unknown>
  if (input.kind === 'show' || input.kind === 'fullscreen') return { kind: input.kind }
  if (
    input.kind === 'game' &&
    typeof input.ownershipId === 'number' &&
    Number.isSafeInteger(input.ownershipId) &&
    input.ownershipId > 0
  )
    return { kind: 'game', ownershipId: input.ownershipId }
  if (input.kind === 'plugin' && typeof input.pluginId === 'string' && typeof input.releaseTag === 'string')
    return (
      pluginInstallLink(`winnow://plugins/install?id=${input.pluginId}&release=${input.releaseTag}`) ?? {
        kind: 'show',
      }
    )
  return { kind: 'show' }
}

export function pluginInstallLink(value: string): Extract<ApplicationActivation, { kind: 'plugin' }> | null {
  if (value.length > 256 || !value.startsWith('winnow://plugins/install?')) return null
  const fields = value.slice('winnow://plugins/install?'.length).split('&')
  if (fields.length !== 2) return null
  const values = new Map(
    fields.map((field) => {
      const i = field.indexOf('=')
      return [field.slice(0, i), field.slice(i + 1)]
    }),
  )
  if (values.size !== 2 || !values.has('id') || !values.has('release')) return null
  const pluginId = values.get('id')!,
    releaseTag = values.get('release')!
  if (
    !['xbox', 'psn', 'steamgriddb'].includes(pluginId) ||
    releaseTag.length > 80 ||
    !/^v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$/.test(
      releaseTag,
    )
  )
    return null
  return { kind: 'plugin', pluginId, releaseTag }
}

export function readActivation(args: string[]): ApplicationActivation {
  const uri = args.findIndex(
    (value) => value === '--uri' || /^winnow:/i.test(value) || value.startsWith('--uri='),
  )
  if (uri >= 0) {
    const index = uri + (args[uri] === '--uri' ? 1 : 0)
    const prefix = args.slice(0, uri)
    if (
      index !== args.length - 1 ||
      (prefix.length && !(prefix.length === 2 && prefix[0] === '--data-dir' && prefix[1].trim()))
    )
      return { kind: 'show' }
    return pluginInstallLink(args[index]) ?? { kind: 'show' }
  }
  const actions = args.filter((value) => value.startsWith('--jump-list-'))
  if (actions.length !== 1) return { kind: 'show' }
  if (actions[0] === '--jump-list-fullscreen') return { kind: 'fullscreen' }
  if (actions[0] === '--jump-list-game') {
    const value = args[args.indexOf(actions[0]) + 1]
    if (/^[1-9]\d*$/.test(value ?? '') && Number.isSafeInteger(Number(value)))
      return { kind: 'game', ownershipId: Number(value) }
  }
  return { kind: 'show' }
}

/** Windows command-line quoting preserves spaces, quotes and trailing slashes in isolated data paths. */
export function quoteArgument(value: string): string {
  return `"${value.replace(/(\\*)"/g, '$1$1\\"').replace(/(\\+)$/, '$1$1')}"`
}

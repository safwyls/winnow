import { isAbsolute } from 'node:path'

export interface InstallationWorkspace {
  ownerships?: { id: number; installed: boolean; installPath?: string | null }[]
}

/** The renderer supplies identity only; the current backend record supplies the path. */
export async function openInstallFolder(
  ownershipId: unknown,
  adapters: {
    workspace(): Promise<InstallationWorkspace>
    isDirectory(path: string): Promise<boolean>
    openPath(path: string): Promise<string>
  },
) {
  if (typeof ownershipId !== 'number' || !Number.isSafeInteger(ownershipId) || ownershipId <= 0)
    throw new Error('Choose a game from your library.')
  const workspace = await adapters.workspace()
  const ownership = workspace.ownerships?.find((entry) => entry.id === ownershipId)
  if (!ownership?.installed) throw new Error('This copy is no longer installed.')
  const path = ownership.installPath
  if (!path?.trim() || !isAbsolute(path) || /[\u0000-\u001f]/.test(path))
    throw new Error('This copy has no recorded installation folder.')
  if (!(await adapters.isDirectory(path).catch(() => false)))
    throw new Error('The installation folder is no longer available.')
  if (await adapters.openPath(path)) throw new Error('The installation folder could not be opened.')
}

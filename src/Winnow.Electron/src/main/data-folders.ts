import { dirname, join } from 'node:path'

interface DataFolderPorts {
  pluginDirectory: () => Promise<string | undefined>
  createDirectory: (path: string) => Promise<unknown>
  openPath: (path: string) => Promise<string>
}

export async function openDataFolder(folder: string, ports: DataFolderPorts) {
  if (!['logs', 'plugins', 'themes'].includes(folder)) throw new Error('Unknown Winnow folder')
  // The backend knows whether the data directory migrated or remains at its legacy location.
  const plugins = await ports.pluginDirectory()
  if (!plugins) throw new Error('Connect to your library before opening its folder.')
  const path = join(dirname(plugins), folder)
  try {
    await ports.createDirectory(path)
    const problem = await ports.openPath(path)
    if (problem) throw new Error('The folder could not be opened.')
  } catch (error) {
    // This path is returned only to the local UI for manual opening, never to diagnostics.
    if (folder === 'logs') throw new Error(`Couldn't open the logs folder. Open it manually: ${path}`)
    throw error
  }
}

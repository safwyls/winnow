/** Electron quotes the executable and arguments when serializing Windows registration. */
export function loginItemOptions(executable: string, explicitDataDirectory?: string) {
  return {
    path: executable,
    args: ['--background', ...(explicitDataDirectory ? ['--data-dir', explicitDataDirectory] : [])],
  }
}

export type ExecutableTitleSource = 'none' | 'file-description' | 'product-name' | 'folder-name' | 'file-name'

/** Proposals read from the selected file; none of these fields authorizes launching it. */
export interface ExecutableFacts {
  executablePath: string
  installPath: string | null
  title: string | null
  titleSource: ExecutableTitleSource
  publisher: string | null
}

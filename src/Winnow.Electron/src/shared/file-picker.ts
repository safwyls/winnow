export interface FilePickerEntry {
  id: string
  name: string
  directory: boolean
}

/** Paths are display-only; actions refer to main-owned entries in the active chooser. */
export interface FilePickerSnapshot {
  id: string
  title: string
  mode: 'open' | 'save' | 'directory'
  directory: string | null
  suggestedName?: string
  entries: FilePickerEntry[]
  page: number
  pages: number
  loading: boolean
  error?: string
  replaceName?: string
}

export interface FilePickerAction {
  id: string
  action: 'entry' | 'parent' | 'previous' | 'next' | 'save' | 'directory' | 'replace' | 'back' | 'cancel'
  entryId?: string
  name?: string
}

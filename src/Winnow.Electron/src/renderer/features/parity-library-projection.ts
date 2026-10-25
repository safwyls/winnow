import { useMemo } from 'react'
import type { LibraryGame } from '../api/types'
import { useLibrary, useWorkspace } from '../api/hooks'
import { usePresentationPreferences } from './SettingsPreferences'

import { parseExpansionGrouping, projectLibraryGames, type LibraryIdentityLink } from './parity-library-grain'
export {
  parseExpansionGrouping,
  projectLibraryGames,
  type LibraryIdentityLink,
  type ExpansionMark,
} from './parity-library-grain'

const emptyGames: LibraryGame[] = []
const emptyLinks: LibraryIdentityLink[] = []

/** Shared by Library, rail counts, search and Settings; raw backend games remain untouched. */
export function useLibraryProjection(source?: LibraryGame[]) {
  const library = useLibrary()
  const workspace = useWorkspace()
  const preferences = usePresentationPreferences()
  const games = source ?? library.data?.games ?? emptyGames
  const links = (workspace.data?.identityLinks as LibraryIdentityLink[] | undefined) ?? emptyLinks
  const enabled = parseExpansionGrouping(preferences.values.GroupExpansions)
  return useMemo(() => projectLibraryGames(games, links, enabled), [games, links, enabled])
}

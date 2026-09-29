import type { ThemePage } from '../shared/theme'

interface Origin {
  page: ThemePage
  workId: number | null
  previous: ThemePage
}
export interface NavigationPosition extends Origin {
  searchOrigin?: Origin
}

export function navigatePosition(current: NavigationPosition, page: ThemePage): NavigationPosition {
  if (page === current.page) return current
  const fromSearchDetails = current.page === 'details' && current.previous === 'search'
  return {
    ...current,
    ...(page === 'search' && !fromSearchDetails
      ? { searchOrigin: { page: current.page, workId: current.workId, previous: current.previous } }
      : {}),
    previous: current.page,
    page,
  }
}

/** Details opened from Search must not replace the page and game that originally opened Search. */
export function returnFromSearch(current: NavigationPosition): NavigationPosition {
  return current.searchOrigin ?? { page: 'library', previous: 'search', workId: null }
}

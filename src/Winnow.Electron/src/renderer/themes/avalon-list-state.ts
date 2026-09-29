import { useEffect } from 'react'
import type { GameList, LibraryFilter, Mode } from '../api/types'
import { useViewState } from '../viewState'

export function filterFingerprint(filter: LibraryFilter) {
  return JSON.stringify(
    Object.fromEntries(
      Object.entries(filter)
        .filter(([, value]) => value != null && value !== '' && (!Array.isArray(value) || value.length))
        .sort(([a], [b]) => a.localeCompare(b))
        .map(([key, value]) => [key, Array.isArray(value) ? [...value].sort() : value]),
    ),
  )
}

const defaultSorts: Record<string, string> = {
  DormantLongest: 'dormant',
  RecentlyPlayed: 'recent',
  PlaytimeHighToLow: 'time',
  PlaytimeLowToHigh: 'time-low',
  NameAscending: 'title',
  NameDescending: 'title-desc',
}

export function libraryDefaultSort(value: string | null | undefined) {
  return defaultSorts[value ?? 'DormantLongest'] ?? 'dormant'
}

/** Collections and the library share one transition policy, including navigation from another page. */
export function useAvalonLists(mode: Mode, lists: GameList[], ready = true, defaultSort?: string) {
  const prefix = `avalon:library:${mode}`
  const [query, setQuery] = useViewState(`${prefix}:query`, '')
  const [bucket, setBucket] = useViewState(`${prefix}:bucket`, 'all')
  const [store, setStore] = useViewState(`${prefix}:store`, 'all')
  const [listId, setListId] = useViewState(`${prefix}:list`, 'all')
  const [savedSort, setSort] = useViewState<string | null>(`${prefix}:sort`, null)
  const [observedDefault, setObservedDefault] = useViewState<string | null>(`${prefix}:default-sort`, null)
  const [previousSort, setPreviousSort] = useViewState<{ value: string | null } | null>(
    `${prefix}:sort-before-list`,
    null,
  )
  const [rules, setRules] = useViewState<LibraryFilter>(`${prefix}:rules`, {})
  const [filtersOpen, setFiltersOpen] = useViewState(`${prefix}:filters-open`, false)
  const [base, setBase] = useViewState<GameList | null>(`${prefix}:list-base`, null)
  const list = lists.find((item) => String(item.id) === listId)
  useEffect(() => {
    if (defaultSort === undefined || defaultSort === observedDefault) return
    // A saved default changes the current order even after a temporary choice. A
    // manual list keeps its own order and receives the new default when it closes.
    if (observedDefault !== null) {
      if (previousSort) setPreviousSort({ value: defaultSort })
      else setSort(defaultSort)
    }
    setObservedDefault(defaultSort)
  }, [defaultSort, observedDefault, previousSort, setPreviousSort, setSort, setObservedDefault])
  const filter: LibraryFilter = {
    ...rules,
    search: query.trim() || null,
    ...(store !== 'all' ? { stores: [store] } : {}),
    ...(bucket === 'installed' ? { installed: true } : bucket !== 'all' ? { buckets: [bucket] } : {}),
  }
  const dirty =
    !!list?.isLive && filterFingerprint(filter) !== filterFingerprint(base?.filter ?? list.filter ?? {})
  function loadRules(value: LibraryFilter) {
    const { search, buckets, ...rest } = value
    setQuery(search ?? '')
    setBucket(buckets?.length === 1 ? buckets[0] : 'all')
    setStore('all')
    setRules(buckets && buckets.length > 1 ? { ...rest, buckets } : rest)
  }
  function leave() {
    if (base?.isLive || list?.isLive) loadRules({})
    if (previousSort) {
      setSort(previousSort.value)
      setPreviousSort(null)
    }
    setListId('all')
    setBase(null)
  }
  function selectList(id: string) {
    if (id === 'all' || id === listId) {
      leave()
      return
    }
    const target = lists.find((item) => String(item.id) === id)
    if (!target) return
    if (base?.isLive || list?.isLive) loadRules({})
    if (target.isLive) {
      if (previousSort) {
        setSort(previousSort.value)
        setPreviousSort(null)
      }
      loadRules(target.filter ?? {})
      if (mode === 'desktop') setFiltersOpen(true)
    } else {
      if (!previousSort) setPreviousSort({ value: savedSort })
      setSort('list-order')
      setBucket('all')
    }
    setListId(id)
    setBase(target)
  }
  function selectBucket(value: string) {
    const toggleOff = !list?.isLive && value !== 'all' && bucket === value
    leave()
    setRules((current) => ({ ...current, buckets: [] }))
    setBucket(toggleOff ? 'all' : value)
  }
  function revert() {
    if (list?.isLive) {
      loadRules(list.filter ?? {})
      setBase(list)
    }
  }
  useEffect(() => {
    if (ready && !list && base && listId !== 'all') {
      leave()
      return
    }
    if (list?.isLive && base?.id === list.id && list.revision !== base.revision && !dirty) {
      loadRules(list.filter ?? {})
      setBase(list)
    }
  }, [list?.revision, base?.revision, dirty, ready, listId])
  return {
    lists,
    filtersOpen,
    setFiltersOpen,
    query,
    setQuery,
    bucket,
    setBucket,
    store,
    setStore,
    listId,
    savedSort,
    setSort,
    rules,
    setRules,
    applyFilter: loadRules,
    list,
    base,
    setBase,
    filter,
    dirty,
    selectList,
    selectBucket,
    revert,
  }
}

import { useEffect, useSyncExternalStore } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { ApiError, request } from '../api/client'
import type { GameList, LibraryGame, LibraryResponse } from '../api/types'

interface MembershipState {
  wanted: boolean
  committed: boolean
  busy: boolean
  blocked: boolean
  problem: string | null
}
const controllers = new WeakMap<QueryClient, Map<string, MembershipController>>()

/** Keep the latest intent while a write is pending; each subsequent write uses its committed revision. */
class MembershipController {
  private list: GameList
  private releaseIds: number[]
  private listeners = new Set<() => void>()
  private state: MembershipState
  constructor(
    private client: QueryClient,
    list: GameList,
    game: LibraryGame,
  ) {
    this.list = list
    this.releaseIds = game.entries.map((entry) => entry.releaseId)
    const member = this.member(list)
    this.state = { wanted: member, committed: member, busy: false, blocked: false, problem: null }
  }
  snapshot = () => this.state
  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }
  private publish(value: Partial<MembershipState>) {
    this.state = { ...this.state, ...value }
    this.listeners.forEach((listener) => listener())
  }
  private member(list: GameList) {
    return list.releaseIds.some((id) => this.releaseIds.includes(id))
  }
  observe(list: GameList, game: LibraryGame) {
    if (this.state.busy) return
    this.releaseIds = game.entries.map((entry) => entry.releaseId)
    this.list = list
    const member = this.member(list)
    if (member !== this.state.committed || member !== this.state.wanted)
      this.publish({ wanted: member, committed: member })
  }
  toggle = (wanted: boolean) => {
    if (this.state.blocked || this.list.isLive || !this.releaseIds.length) return
    this.publish({ wanted, problem: this.state.busy ? this.state.problem : null })
    if (!this.state.busy) void this.flush()
  }
  private async applySaved(list: GameList) {
    if (list.id !== this.list.id || !Array.isArray(list.releaseIds) || !list.revision)
      throw new Error('The saved list could not be checked.')
    this.list = list
    // A snapshot that started before this write cannot replace the returned committed result.
    await this.client.cancelQueries({ queryKey: ['api', 'library.get'] })
    this.client.setQueryData<LibraryResponse>(
      ['api', 'library.get'],
      (previous) =>
        previous && {
          ...previous,
          lists: previous.lists.map((value) => (value.id === list.id ? list : value)),
        },
    )
    this.publish({ committed: this.member(list) })
  }
  private async readSaved() {
    const saved = await request<LibraryResponse>('library.get')
    const list = saved.lists.find((value) => value.id === this.list.id)
    if (!list || list.isLive) throw new Error('This handpicked list is no longer available.')
    await this.applySaved(list)
  }
  check = async () => {
    if (this.state.busy) return
    this.publish({ busy: true })
    try {
      await this.readSaved()
      this.publish({ wanted: this.state.committed, blocked: false })
    } catch {
      this.publish({
        problem: 'The saved list could not be checked. Your latest choice is kept here.',
        blocked: true,
      })
    } finally {
      this.publish({ busy: false })
    }
  }
  private async flush() {
    this.publish({ busy: true, problem: null })
    try {
      while (this.state.wanted !== this.state.committed) {
        const wanted = this.state.wanted
        const result = await request<GameList>(
          wanted ? 'list.member.add' : 'list.member.remove',
          { listId: this.list.id },
          {
            releaseIds: wanted
              ? [this.releaseIds[0]!]
              : this.list.releaseIds.filter((id) => this.releaseIds.includes(id)),
            expectedRevision: this.list.revision,
          },
        )
        await this.applySaved(result)
        if (this.state.committed !== wanted)
          throw new Error('The list did not store the requested membership.')
      }
    } catch (error) {
      const conflict = error instanceof ApiError && error.conflict
      if (!(error instanceof ApiError) || error.uncertain || conflict) {
        try {
          await this.readSaved()
        } catch {
          this.publish({ blocked: true })
        }
      }
      this.publish({
        wanted: this.state.blocked ? this.state.wanted : this.state.committed,
        problem: conflict
          ? 'This list changed in another window. Review its saved contents before trying again.'
          : "Couldn't save list changes. Try again.",
      })
    } finally {
      this.publish({ busy: false })
    }
  }
}

export function ListMembershipChoice({ list, game }: { list: GameList; game: LibraryGame }) {
  const client = useQueryClient()
  let registry = controllers.get(client)
  if (!registry) {
    registry = new Map()
    controllers.set(client, registry)
  }
  const key = `${list.id}:${game.workId}`
  let controller = registry.get(key)
  if (!controller) {
    controller = new MembershipController(client, list, game)
    registry.set(key, controller)
  }
  const state = useSyncExternalStore(controller.subscribe, controller.snapshot, controller.snapshot)
  useEffect(() => controller.observe(list, game), [controller, list, game])
  if (list.isLive)
    return (
      <p>
        {list.name} · Live list{' '}
        {list.releaseIds.some((id) => game.entries.some((entry) => entry.releaseId === id))
          ? '· Included by its filters'
          : '· Chosen by its filters'}
      </p>
    )
  return (
    <div className="list-membership-choice">
      <label className="check-field">
        <input
          type="checkbox"
          aria-label={`${state.wanted ? 'Remove from' : 'Add to'} ${list.name}`}
          checked={state.wanted}
          disabled={state.blocked}
          onChange={(event) => controller.toggle(event.target.checked)}
        />
        {list.name}
      </label>
      {state.busy ? (
        <p role="status">Saving list changes…</p>
      ) : state.problem ? (
        <p role="alert">{state.problem}</p>
      ) : null}
      {state.blocked && (
        <div className="conflict-panel">
          <p>The last response was interrupted. Check the saved membership before making another change.</p>
          <button disabled={state.busy} onClick={() => void controller.check()}>
            Check saved list
          </button>
        </div>
      )}
    </div>
  )
}

import { useSyncExternalStore } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { request } from '../api/client'
import type { PresentationPreferenceValue } from './SettingsPreferences'

const key = ['api', 'preferences.presentation.get', undefined] as const

/** Keep live range input independent of disk latency, with at most one write and one latest value. */
class ContinuousPreference {
  private listeners = new Set<() => void>()
  private next: string | undefined
  private confirmed: string | null = null
  private state: { pending: boolean; error: unknown; value?: string } = { pending: false, error: null }

  constructor(
    private readonly client: QueryClient,
    private readonly preference: string,
  ) {}

  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }
  snapshot = () => this.state
  private publish(pending: boolean, error: unknown = null, value?: string) {
    this.state = { pending, error, value }
    for (const listener of this.listeners) listener()
  }
  private display(value: string | null) {
    this.client.setQueryData<PresentationPreferenceValue[]>(key, (rows) => [
      ...(rows ?? []).filter((row) => row.preference !== this.preference),
      { preference: this.preference, value },
    ])
  }
  set = (value: string) => {
    if (!this.state.pending)
      this.confirmed =
        this.client
          .getQueryData<PresentationPreferenceValue[]>(key)
          ?.find((row) => row.preference === this.preference)?.value ?? null
    this.next = value
    void this.client.cancelQueries({ queryKey: key, exact: true })
    this.display(value)
    const running = this.state.pending
    this.publish(true, null, value)
    if (!running) void this.flush()
  }
  private async flush() {
    let failure: unknown = null
    while (this.next !== undefined) {
      const value = this.next
      this.next = undefined
      try {
        await request('preferences.presentation.put', { preference: this.preference }, { value })
        this.confirmed = value
        failure = null
      } catch (error) {
        failure = error
      }
      // A completed older request cannot replace a newer position under the pointer.
      if (this.next === undefined) {
        void this.client.cancelQueries({ queryKey: key, exact: true })
        this.display(this.confirmed)
      }
    }
    this.publish(false, failure)
  }
}

const owners = new WeakMap<QueryClient, Map<string, ContinuousPreference>>()

export function useContinuousPresentationPreference(preference: string) {
  const client = useQueryClient()
  let preferences = owners.get(client)
  if (!preferences) owners.set(client, (preferences = new Map()))
  let owner = preferences.get(preference)
  if (!owner) preferences.set(preference, (owner = new ContinuousPreference(client, preference)))
  const state = useSyncExternalStore(owner.subscribe, owner.snapshot)
  const value = state.pending
    ? state.value
    : client.getQueryData<PresentationPreferenceValue[]>(key)?.find((row) => row.preference === preference)
        ?.value
  return { ...state, value, set: owner.set }
}

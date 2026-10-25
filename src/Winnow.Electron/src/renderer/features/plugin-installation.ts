import { useSyncExternalStore } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { createClientId, request } from '../api/client'
import type { PluginSnapshot } from '../api/types'

export interface PluginInstallRequest {
  pluginId: string
  releaseTag: string
}
interface InstallOperation {
  state: string
  message: string
  pluginResult?: { outcome: number; pluginId: string; message: string } | null
}
export interface PluginInstallationState {
  request: PluginInstallRequest | null
  operationId: string | null
  busy: boolean
  canRetry: boolean
  status: string
  completedPlugin?: PluginSnapshot
}
interface Presentation {
  started?(): void
  ready?(plugin: PluginSnapshot): void
}
const failure = 'Could not finish the plugin installation. Try again.'
const cancelled = 'Plugin installation cancelled. Try again when you’re ready.'

/** One operation outlives its screen; each retry keeps an uncertain backend identity. */
export class PluginInstallation {
  private state: PluginInstallationState = {
    request: null,
    operationId: null,
    busy: false,
    canRetry: false,
    status: '',
  }
  private listeners = new Set<() => void>()
  private tail: Promise<void> = Promise.resolve()
  private waiting = 0
  private terminal = false
  private cancelRequested = false
  private cancelSent = false
  private presentation: Presentation = {}
  private refreshed = false
  constructor(private readonly client: QueryClient) {}
  snapshot = () => this.state
  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }
  private publish(patch: Partial<PluginInstallationState>) {
    this.state = { ...this.state, ...patch }
    this.listeners.forEach((listener) => listener())
  }
  install(value: PluginInstallRequest, presentation: Presentation = {}) {
    value = { pluginId: value.pluginId, releaseTag: value.releaseTag }
    this.waiting++
    const result = this.tail.then(async () => {
      this.waiting--
      this.presentation = presentation
      this.refreshed = false
      await this.run(value, createClientId(), true)
    })
    this.tail = result.catch(() => undefined)
    return result
  }
  retry() {
    if (!this.state.canRetry || this.state.busy || this.waiting || !this.state.request)
      return Promise.resolve()
    const value = this.state.request
    const id = this.terminal ? createClientId() : (this.state.operationId ?? createClientId())
    if (this.terminal) this.refreshed = false
    this.cancelRequested = false
    this.cancelSent = false
    // Acquire the queue synchronously so repeated input cannot enqueue duplicate retries.
    this.publish({ busy: true, canRetry: false })
    const result = this.tail.then(() => this.run(value, id, false))
    this.tail = result.catch(() => undefined)
    return result
  }
  async cancel() {
    if (!this.state.busy) return
    this.cancelRequested = true
    this.publish({ status: 'Canceling plugin installation…' })
    // A pending start may not have created its operation yet. run sends this after acceptance.
  }
  private async run(value: PluginInstallRequest, id: string, showProgress: boolean) {
    this.terminal = false
    if (showProgress) {
      this.cancelRequested = false
      this.cancelSent = false
    }
    this.publish({
      request: value,
      operationId: id,
      busy: true,
      canRetry: false,
      completedPlugin: undefined,
      status: 'Preparing plugin installation…',
    })
    try {
      if (showProgress) this.presentation.started?.()
      let operation = await request<InstallOperation>('operations.plugin', undefined, {
        operationId: id,
        request: value,
      })
      while (operation && ['queued', 'running'].includes(operation.state?.toLowerCase())) {
        if (this.cancelRequested && !this.cancelSent) {
          await request('operations.cancel', { id })
          this.cancelSent = true
        }
        if (!this.cancelRequested) this.publish({ status: operation.message })
        await new Promise((done) => setTimeout(done, 1500))
        operation = await request<InstallOperation>('operations.detail', { id })
      }
      if (!operation?.state) throw new Error('Missing operation result')
      this.terminal = true
      if (operation.state.toLowerCase() === 'cancelled') {
        this.publish({ status: cancelled, canRetry: true })
        return
      }
      const result = operation.pluginResult
      if (operation.state.toLowerCase() !== 'completed' || !result || result.pluginId !== value.pluginId)
        throw new Error('Incomplete installation')
      if (result.outcome === 2) {
        this.publish({ status: result.message || failure, canRetry: true })
        return
      }
      if (result.outcome !== 0 && result.outcome !== 1) throw new Error('Unknown installation result')
      // Retrying publication must not reinstall code or repeat a successful refresh.
      this.terminal = false
      if (result.outcome === 0 && !this.refreshed) {
        await request('plugins.refresh', { pluginId: result.pluginId })
        this.refreshed = true
      }
      await this.client.cancelQueries({ queryKey: ['api', 'plugins.get'] })
      const plugins = await request<PluginSnapshot[]>('plugins.get')
      const plugin = plugins.find((candidate) => candidate.id === result.pluginId)
      if (!plugin) throw new Error('Installed settings unavailable')
      this.client.setQueryData(['api', 'plugins.get', undefined], plugins)
      this.publish({ status: result.message || 'Plugin settings are ready.', completedPlugin: plugin })
      this.presentation.ready?.(plugin)
      this.terminal = true
    } catch {
      this.publish({ status: failure, canRetry: true })
    } finally {
      this.publish({ busy: false })
      void this.client.invalidateQueries({ queryKey: ['api', 'operations.get'] })
    }
  }
}

const installations = new WeakMap<QueryClient, PluginInstallation>()
export function pluginInstallation(client: QueryClient) {
  let installation = installations.get(client)
  if (!installation) {
    installation = new PluginInstallation(client)
    installations.set(client, installation)
  }
  return installation
}
export function usePluginInstallation() {
  const installation = pluginInstallation(useQueryClient())
  const state = useSyncExternalStore(installation.subscribe, installation.snapshot, installation.snapshot)
  return { installation, ...state }
}

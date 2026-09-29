import { readFile, stat } from 'node:fs/promises'
import { join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import type { ApiRequest, ApiResult, BackendEvent, ConnectionState } from '../shared/bridge'
import { resolveRoute } from './routes'
import { artworkFileLimit, validArtworkImport } from './artwork-import'
import type { ArtworkImport, ArtworkSaveResult } from '../shared/bridge'

export interface Discovery {
  address: string
  token: string
  epoch: string
  apiVersion: string
  processId: number
}
export function validateDiscovery(value: unknown): Discovery {
  if (!value || typeof value !== 'object') throw new Error('Invalid backend discovery')
  const entry = value as Discovery
  if (entry.apiVersion !== '1') throw new Error('This backend uses an unsupported API version')
  const address = new URL(entry.address)
  if (
    address.protocol !== 'http:' ||
    address.hostname !== '127.0.0.1' ||
    !address.port ||
    address.username ||
    address.password ||
    address.pathname !== '/' ||
    address.search ||
    address.hash
  )
    throw new Error('Invalid local backend address')
  // Comparing the source avoids URL normalization accepting alternate numeric IP spellings.
  if (!/^http:\/\/127\.0\.0\.1:[1-9]\d{0,4}\/?$/.test(entry.address))
    throw new Error('Invalid local backend address')
  if (
    typeof entry.token !== 'string' ||
    entry.token.length < 1 ||
    entry.token.length > 4096 ||
    /\s|[\u0000-\u001f\u007f]/.test(entry.token)
  )
    throw new Error('Invalid backend credential')
  if (
    typeof entry.epoch !== 'string' ||
    !/^[a-zA-Z0-9-]{1,128}$/.test(entry.epoch) ||
    !Number.isSafeInteger(entry.processId) ||
    entry.processId <= 0
  )
    throw new Error('Invalid backend identity')
  return { ...entry, address: address.href }
}
export async function readDiscovery(directory: string): Promise<Discovery> {
  const path = join(directory, 'backend', 'endpoint.json')
  if ((await stat(path)).size > 16384) throw new Error('Invalid backend discovery')
  return validateDiscovery(JSON.parse(await readFile(path, 'utf8')))
}

export interface EventFrame {
  id?: string
  event?: string
  data: string
}
export async function* parseEvents(stream: ReadableStream<Uint8Array>): AsyncGenerator<EventFrame> {
  const reader = stream.getReader()
  const decoder = new TextDecoder()
  let pending = ''
  let data: string[] = []
  let id: string | undefined
  let event: string | undefined
  try {
    while (true) {
      const next = await reader.read()
      if (next.done) break
      pending += decoder.decode(next.value, { stream: true })
      if (pending.length > 1024 * 1024) throw new Error('Backend event exceeds the size limit')
      let end: number
      while ((end = pending.indexOf('\n')) >= 0) {
        const line = pending.slice(0, end).replace(/\r$/, '')
        pending = pending.slice(end + 1)
        if (line === '') {
          if (data.length) yield { id, event, data: data.join('\n') }
          data = []
          id = undefined
          event = undefined
        } else if (!line.startsWith(':')) {
          const colon = line.indexOf(':')
          const field = colon < 0 ? line : line.slice(0, colon)
          const value = colon < 0 ? '' : line.slice(colon + 1).replace(/^ /, '')
          if (field === 'data') data.push(value)
          if (field === 'id') id = value
          if (field === 'event') event = value
          if (data.reduce((size, item) => size + item.length, 0) > 1024 * 1024)
            throw new Error('Backend event exceeds the size limit')
        }
      }
    }
  } finally {
    await reader.cancel().catch(() => undefined)
    reader.releaseLock()
  }
}

export class EventCursor {
  value?: string
  consume(frame: EventFrame): BackendEvent | null {
    const event = JSON.parse(frame.data) as BackendEvent
    if (
      typeof event.kind !== 'string' ||
      event.kind.length > 128 ||
      typeof event.epoch !== 'string' ||
      !/^[a-zA-Z0-9-]{1,128}$/.test(event.epoch) ||
      !Number.isSafeInteger(event.sequence) ||
      event.sequence! < 0
    )
      throw new Error('Invalid backend event')
    const cursor = `${event.epoch}:${event.sequence}`
    if (frame.id !== cursor) throw new Error('Backend event cursor does not match its payload')
    if (this.value && event.kind !== 'resync-required') {
      const [epoch, sequence] = this.value.split(':')
      if (epoch === event.epoch && event.sequence! <= Number(sequence)) return null
      if (epoch !== event.epoch || event.sequence !== Number(sequence) + 1) {
        event.kind = 'resync-required'
        delete event.resource
      }
    }
    this.value = cursor
    return event
  }
}

type Fetch = typeof fetch
interface TransportOptions {
  discover: () => Promise<Discovery>
  onEvent?: (event: BackendEvent) => void
  onConnection?: (state: ConnectionState) => void
  fetch?: Fetch
  reconnectMs?: number
}

export class BackendTransport {
  private readonly lifetime = new AbortController()
  private readonly cursor = new EventCursor()
  private readonly fetcher: Fetch
  private active?: Discovery
  private attempt?: AbortController
  private task?: Promise<void>
  private waiters = new Set<() => void>()
  private startupProblem?: string
  private startupTimer?: ReturnType<typeof setTimeout>
  private hasConnected = false
  private state: ConnectionState = { connected: false, message: 'Connecting to your library…' }
  constructor(private readonly options: TransportOptions) {
    this.fetcher = options.fetch ?? fetch
  }
  connection(): ConnectionState {
    return { ...this.state }
  }
  start(): void {
    if (this.task) return
    this.startupTimer = setTimeout(() => {
      if (!this.hasConnected && !this.startupProblem)
        this.setStartupProblem(
          'The backend has not connected after 45 seconds. Check that the Winnow backend started successfully; this window will keep trying. Your edits have not been sent.',
        )
    }, 45000)
    this.startupTimer.unref?.()
    this.task = this.watch()
  }
  stop(): void {
    this.lifetime.abort()
    this.attempt?.abort()
    this.active = undefined
    clearTimeout(this.startupTimer)
  }
  setStartupProblem(message: string): void {
    this.startupProblem = message
    if (!this.state.connected) this.setState({ connected: false, message })
  }
  private setState(state: ConnectionState): void {
    if (state.connected) {
      this.hasConnected = true
      this.startupProblem = undefined
      clearTimeout(this.startupTimer)
    }
    const changed =
      state.connected !== this.state.connected ||
      state.message !== this.state.message ||
      state.epoch !== this.state.epoch
    this.state = state
    if (changed) this.options.onConnection?.({ ...state })
    if (state.connected) {
      for (const ready of this.waiters) ready()
      this.waiters.clear()
    }
  }
  private async ready(requestSignal?: AbortSignal): Promise<Discovery> {
    const signal = requestSignal ? AbortSignal.any([this.lifetime.signal, requestSignal]) : this.lifetime.signal
    signal.throwIfAborted()
    this.start()
    if (!this.active || !this.state.connected)
      await new Promise<void>((resolve, reject) => {
        const ready = () => {
          clearTimeout(timer)
          this.waiters.delete(ready)
          signal.removeEventListener('abort', aborted)
          resolve()
        }
        const aborted = () => {
          clearTimeout(timer)
          this.waiters.delete(ready)
          signal.removeEventListener('abort', aborted)
          reject(signal.reason)
        }
        const timer = setTimeout(() => {
          this.waiters.delete(ready)
          signal.removeEventListener('abort', aborted)
          reject(new Error('The backend is unavailable. Your edits have not been sent.'))
        }, 12000)
        this.waiters.add(ready)
        signal.addEventListener('abort', aborted, { once: true })
        if (signal.aborted) aborted()
      })
    signal.throwIfAborted()
    if (!this.active) throw new Error('The backend is unavailable')
    return this.active
  }
  private async watch(): Promise<void> {
    while (!this.lifetime.signal.aborted) {
      try {
        const connection = validateDiscovery(await this.options.discover())
        const controller = new AbortController()
        this.attempt = controller
        const headers: Record<string, string> = {
          Authorization: `Bearer ${connection.token}`,
          Accept: 'text/event-stream',
        }
        if (this.cursor.value) headers['Last-Event-ID'] = this.cursor.value
        const handshakeTimeout = setTimeout(() => controller.abort(), 15000)
        let response: Response
        try {
          response = await this.fetcher(new URL('/api/v1/events', connection.address), {
            headers,
            redirect: 'error',
            signal: AbortSignal.any([controller.signal, this.lifetime.signal]),
          })
        } finally {
          clearTimeout(handshakeTimeout)
        }
        if (
          !response.ok ||
          !response.body ||
          !response.headers.get('content-type')?.startsWith('text/event-stream')
        )
          throw new Error('Backend events are unavailable')
        this.active = connection
        // Response headers are flushed only after the backend atomically registers this subscription.
        // On first attach wait for its initial marker; a replay cursor may have no new events.
        if (this.cursor.value)
          this.setState({ connected: true, message: 'Library connected', epoch: connection.epoch })
        for await (const frame of parseEvents(response.body)) {
          const event = this.cursor.consume(frame)
          if (!event) continue
          this.options.onEvent?.(event)
          this.setState({ connected: true, message: 'Library connected', epoch: event.epoch })
        }
      } catch {
        /* Discovery and auth errors stay in main; never forward credentials or raw requests. */
      }
      this.active = undefined
      if (this.lifetime.signal.aborted) break
      this.setState({
        connected: false,
        message:
          this.startupProblem ??
          (this.hasConnected
            ? 'Backend disconnected. Reconnecting; your unsaved edits stay here.'
            : 'Waiting for the Winnow backend to start. Your edits have not been sent.'),
      })
      await delay(this.options.reconnectMs ?? 1500, undefined, { signal: this.lifetime.signal }).catch(
        () => undefined,
      )
    }
  }
  async request<T = unknown>(request: ApiRequest, requestSignal?: AbortSignal): Promise<ApiResult<T>> {
    const cancelled = (): ApiResult<T> => ({ ok: false, status: 499, message: 'Request cancelled.' })
    if (requestSignal?.aborted) return cancelled()
    let route: ReturnType<typeof resolveRoute>
    try {
      route = resolveRoute(request)
    } catch (error) {
      return { ok: false, status: 400, message: error instanceof Error ? error.message : 'Invalid request' }
    }
    let connection: Discovery
    try {
      connection = await this.ready(requestSignal)
    } catch {
      if (requestSignal?.aborted) return cancelled()
      return { ok: false, status: 503, message: 'The backend is unavailable. Reconnect before trying again.' }
    }
    try {
      const response = await this.fetcher(new URL(route.path, connection.address), {
        method: route.method,
        headers: {
          Authorization: `Bearer ${connection.token}`,
          Accept: 'application/json',
          ...(route.body ? { 'Content-Type': 'application/json' } : {}),
        },
        body: route.body,
        redirect: 'error',
        signal: AbortSignal.any([this.lifetime.signal, AbortSignal.timeout(120000), ...(requestSignal ? [requestSignal] : [])]),
      })
      if (response.status === 401) this.attempt?.abort()
      const responseText = await response.text()
      requestSignal?.throwIfAborted()
      const text = response.ok ? responseText : responseText.replaceAll(connection.token, '[redacted]')
      const responseLimit = request.route === 'imports.steam.load' ? 180 * 1024 * 1024 : 32 * 1024 * 1024
      if (text.length > responseLimit) throw new Error('Response too large')
      let data: unknown
      try {
        data = text ? JSON.parse(text) : undefined
      } catch {
        data = undefined
      }
      if (!response.ok) {
        const problem = data as { detail?: unknown; title?: unknown } | undefined
        const detail =
          typeof problem?.detail === 'string'
            ? problem.detail
            : typeof problem?.title === 'string'
              ? problem.title
              : `Backend returned ${response.status}`
        return {
          ok: false,
          status: response.status,
          data: data as T,
          message: detail.replaceAll(connection.token, '[redacted]').slice(0, 1000),
        }
      }
      return { ok: true, status: response.status, data: data as T }
    } catch {
      if (requestSignal?.aborted) return cancelled()
      return {
        ok: false,
        status: 0,
        message:
          route.method === 'GET'
            ? 'Could not read the library. Try again after reconnecting.'
            : 'The response was lost. The change may have been saved. Reload current state before trying again.',
      }
    }
  }
  async importArtwork(input: ArtworkImport, bytes: Uint8Array): Promise<ApiResult<ArtworkSaveResult>> {
    if (!validArtworkImport(input) || !bytes.length || bytes.length > artworkFileLimit)
      return { ok: false, status: 400, message: 'Choose a non-empty image no larger than 16 MiB.' }
    let connection: Discovery
    try { connection = await this.ready() }
    catch { return { ok: false, status: 503, message: 'The backend is unavailable. Reconnect before trying again.' } }
    try {
      const query = new URLSearchParams({ revision: input.revision })
      const response = await this.fetcher(new URL(`/api/v1/works/${input.workId}/artwork/${input.slot}/image?${query}`, connection.address), {
        method: 'POST', headers: { Authorization: `Bearer ${connection.token}`, 'Content-Type': 'application/octet-stream', Accept: 'application/json' },
        body: new Uint8Array(bytes).buffer, redirect: 'error', signal: AbortSignal.any([this.lifetime.signal, AbortSignal.timeout(120000)]),
      })
      if (response.status === 401) this.attempt?.abort()
      const text = (await response.text()).replaceAll(connection.token, '[redacted]')
      if (text.length > 1024 * 1024) throw new Error('Response too large')
      const data = text ? JSON.parse(text) : undefined
      if (!response.ok) return { ok: false, status: response.status, data, message: String(data?.detail ?? data?.title ?? `Backend returned ${response.status}`).slice(0, 1000) }
      if (typeof data?.success !== 'boolean' || typeof data?.message !== 'string') throw new Error('Invalid artwork response')
      return { ok: true, status: response.status, data }
    } catch {
      return { ok: false, status: 0, message: 'The response was lost. The image may have been saved. Refresh current artwork before trying again.' }
    }
  }
  async artwork(provider: string, id: string, width = 1280, requestSignal?: AbortSignal): Promise<string | null> {
    if (
      typeof provider !== 'string' ||
      !/^[a-zA-Z0-9:.-]{1,80}$/.test(provider) ||
      typeof id !== 'string' ||
      !/^[a-zA-Z0-9_.-]{1,256}$/.test(id) ||
      !Number.isInteger(width) ||
      width < 64 ||
      width > 3840
    )
      return null
    try {
      requestSignal?.throwIfAborted()
      const connection = await this.ready()
      requestSignal?.throwIfAborted()
      const query = new URLSearchParams({ provider, id, width: String(width) })
      const response = await this.fetcher(new URL(`/api/v1/artwork/image?${query}`, connection.address), {
        headers: { Authorization: `Bearer ${connection.token}` },
        redirect: 'error',
        signal: AbortSignal.any([this.lifetime.signal, AbortSignal.timeout(30000), ...(requestSignal ? [requestSignal] : [])]),
      })
      if (!response.ok || response.headers.get('content-type')?.split(';')[0] !== 'image/png') return null
      const bytes = await response.arrayBuffer()
      requestSignal?.throwIfAborted()
      if (bytes.byteLength > 32 * 1024 * 1024) return null
      return `data:image/png;base64,${Buffer.from(bytes).toString('base64')}`
    } catch {
      return null
    }
  }
}

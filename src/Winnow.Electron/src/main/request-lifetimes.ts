import type { ApiRequest, ApiResult } from '../shared/bridge'

const requestId = /^[0-9a-f]{32}$/

/** Cancellation belongs to one renderer and only to a request still in flight. */
export class RequestLifetimes {
  private readonly owners = new Map<object, Map<string, AbortController>>()

  async run<T>(
    owner: object,
    request: ApiRequest,
    send: (signal?: AbortSignal) => Promise<ApiResult<T>>,
  ): Promise<ApiResult<T>> {
    if (!request || typeof request !== 'object')
      return { ok: false, status: 400, message: 'Invalid request.' }
    if (request.requestId === undefined) return send()
    const id = request.requestId
    if (typeof id !== 'string' || !requestId.test(id))
      return { ok: false, status: 400, message: 'Invalid request identity.' }
    const pending = this.owners.get(owner) ?? new Map<string, AbortController>()
    if (pending.has(id)) return { ok: false, status: 409, message: 'This request is already running.' }
    if (pending.size >= 128) return { ok: false, status: 429, message: 'Too many requests are running.' }
    this.owners.set(owner, pending)
    const controller = new AbortController()
    pending.set(id, controller)
    try {
      return await send(controller.signal)
    } finally {
      pending.delete(id)
      if (!pending.size && this.owners.get(owner) === pending) this.owners.delete(owner)
    }
  }

  cancel(owner: object, id: unknown): boolean {
    if (typeof id !== 'string' || !requestId.test(id)) return false
    const controller = this.owners.get(owner)?.get(id)
    if (!controller || controller.signal.aborted) return false
    controller.abort()
    return true
  }

  close(owner: object): void {
    const pending = this.owners.get(owner)
    this.owners.delete(owner)
    for (const controller of pending?.values() ?? []) controller.abort()
  }
}

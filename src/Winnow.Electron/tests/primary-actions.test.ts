import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../src/renderer/api/client'
import { PrimaryActions } from '../src/renderer/features/PrimaryActions'

const target = { action: 'Play', title: 'Fez', store: 'Steam' }

describe('shared tile and Details primary command', () => {
  it('joins concurrent calls for the same ownership and creates a new operation after completion', async () => {
    let complete!: (result: number) => void
    const send = vi.fn(
      (_id: number, _target: typeof target, _operation: { operationId: string; action: string }) =>
        new Promise<number>((resolve) => {
          complete = resolve
        }),
    )
    const command = new PrimaryActions(() => target, send)
    const tile = command.launch(1)
    const details = command.launch(1)
    expect(details).toBe(tile)
    await Promise.resolve()
    expect(send).toHaveBeenCalledTimes(1)
    const operation = send.mock.calls[0][2]
    complete(0)
    await expect(details).resolves.toBe(0)
    const next = command.launch(1)
    await Promise.resolve()
    expect(send.mock.calls[1][2].operationId).not.toBe(operation.operationId)
    complete(1)
    await next
  })

  it('retries an uncertain action unchanged after the current install state changes', async () => {
    let current = { ...target, action: 'Install' }
    const send = vi
      .fn(
        async (_id: number, _target: typeof target, _operation: { operationId: string; action: string }) => 0,
      )
      .mockRejectedValueOnce(new ApiError(0, 'Interrupted'))
    const command = new PrimaryActions(() => current, send)
    await expect(command.launch(1)).rejects.toThrow('Interrupted')
    current = target
    await command.launch(1)
    expect(send.mock.calls[1]).toEqual(send.mock.calls[0])
    await command.launch(1)
    expect(send.mock.calls[2][2].action).toBe('Play')
    expect(send.mock.calls[2][2].operationId).not.toBe(send.mock.calls[0][2].operationId)
  })

  it('keeps distinct ownerships independent and dismisses only completed uncertain attempts', async () => {
    const send = vi
      .fn(
        async (_id: number, _target: typeof target, _operation: { operationId: string; action: string }) => 0,
      )
      .mockRejectedValueOnce(new ApiError(503, 'Interrupted'))
    const command = new PrimaryActions(() => target, send)
    const first = command.launch(1)
    command.dismiss(1)
    expect(command.launch(1)).toBe(first)
    await expect(first).rejects.toThrow('Interrupted')
    command.dismiss(1)
    await Promise.all([command.launch(1), command.launch(2)])
    expect(send.mock.calls.map(([id]) => id)).toEqual([1, 1, 2])
    expect(new Set(send.mock.calls.map(([, , operation]) => operation.operationId)).size).toBe(3)
  })

  it.each([new ApiError(400, 'Refused'), new Error('Unavailable')])(
    'clears a definite failure %s',
    async (failure) => {
      const send = vi
        .fn(
          async (_id: number, _target: typeof target, _operation: { operationId: string; action: string }) =>
            0,
        )
        .mockRejectedValueOnce(failure)
      const command = new PrimaryActions(() => target, send)
      await expect(command.launch(1)).rejects.toThrow()
      await command.launch(1)
      expect(send.mock.calls[1][2].operationId).not.toBe(send.mock.calls[0][2].operationId)
    },
  )

  it('refuses an unsupported current ownership without sending an action', async () => {
    const send = vi.fn()
    const command = new PrimaryActions(() => null, send)
    await expect(command.launch(1)).rejects.toThrow('No supported launch action')
    expect(send).not.toHaveBeenCalled()
  })
})

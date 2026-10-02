import { EventEmitter } from 'node:events'
import { PassThrough } from 'node:stream'
import type { spawn } from 'node:child_process'
import { describe, expect, it, vi } from 'vitest'
import { startDistributionLease } from '../src/main/distribution-helper'

function fixture() {
  const child = Object.assign(new EventEmitter(), {
    stdout: new PassThrough(),
    stderr: new PassThrough(),
    stdin: new PassThrough(),
    kill: vi.fn(),
  })
  const launch = vi.fn(() => child) as unknown as typeof spawn
  const signal = new AbortController(),
    onLost = vi.fn()
  const pending = startDistributionLease({
    helper: 'fixture-helper',
    installation: 'fixture-installation',
    dataDirectory: 'fixture-library',
    signal: signal.signal,
    onLost,
    launch,
  })
  const frame = (value: unknown) => child.stdout.write(`${JSON.stringify(value)}\n`)
  return { child, signal, onLost, pending, frame }
}
describe('parent-bound portable startup guard', () => {
  it('keeps the lease and waits for explicit readiness acknowledgement', async () => {
    const f = fixture()
    f.frame({ kind: 'leased', canUpdate: true, recoveryStatus: null })
    const lease = await f.pending
    expect(lease.canUpdate).toBe(true)
    expect(f.child.stdin.read()).toBeNull()
    const ready = lease.ready()
    expect(String(f.child.stdin.read())).toBe('{"kind":"ready"}\n')
    let done = false
    void ready.then(() => {
      done = true
    })
    await Promise.resolve()
    expect(done).toBe(false)
    f.frame({ kind: 'ready' })
    await ready
    await lease.ready()
    expect(f.child.stdin.read()).toBeNull()
    lease.dispose()
    expect(f.child.kill).toHaveBeenCalledOnce()
    expect(f.onLost).not.toHaveBeenCalled()
  })
  it('retains recovery guidance and read-only eligibility supplied by the guard', async () => {
    const f = fixture()
    f.frame({ kind: 'leased', canUpdate: false, recoveryStatus: 'Paired backup restored.' })
    const lease = await f.pending
    expect(lease.canUpdate).toBe(false)
    expect(lease.recoveryStatus).toBe('Paired backup restored.')
    lease.dispose()
  })
  it('refuses startup when the guard exits before validating the installation', async () => {
    const f = fixture()
    f.child.stderr.write('An interrupted update needs recovery.')
    f.child.emit('exit', 1)
    await expect(f.pending).rejects.toThrow('recovery')
    expect(f.onLost).not.toHaveBeenCalled()
  })
  it('reports lost protection after startup and rejects an unfinished ready handshake', async () => {
    const f = fixture()
    f.frame({ kind: 'leased', canUpdate: true })
    const lease = await f.pending
    const ready = lease.ready()
    f.child.emit('exit', 1)
    await expect(ready).rejects.toThrow('closed')
    expect(f.onLost).toHaveBeenCalledOnce()
  })
  it('holds the acquired guard while normal quit drains, then releases without a startup fault', async () => {
    const f = fixture()
    f.frame({ kind: 'leased', canUpdate: true })
    const lease = await f.pending
    f.signal.abort()
    expect(f.child.kill).not.toHaveBeenCalled()
    lease.dispose()
    f.child.emit('exit', 0)
    expect(f.child.kill).toHaveBeenCalledOnce()
    expect(f.onLost).not.toHaveBeenCalled()
  })
  it.each(['not JSON\n', `${JSON.stringify({ kind: 'ready' })}\n`, 'x'.repeat(17000)])(
    'rejects an invalid first frame',
    async (value) => {
      const f = fixture()
      f.child.stdout.write(value)
      await expect(f.pending).rejects.toThrow()
      expect(f.child.kill).toHaveBeenCalledOnce()
    },
  )
})

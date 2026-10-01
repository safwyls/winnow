import childProcess from 'node:child_process'
import { syncBuiltinESMExports } from 'node:module'
import { connect } from 'node:net'

// Observe the actual helper protocol without changing any bytes or responses.
export function observeActivationHelper(onPrimary) {
  const state = { processId: null, primary: null, frames: [] }
  const original = childProcess.spawn
  childProcess.spawn = function (...args) {
    const child = original.apply(this, args)
    if (!Array.isArray(args[1]) || !args[1].includes('--frontend-activation-helper')) return child
    state.processId = child.pid
    let text = '',
      held = false,
      buffered = []
    const emit = child.stdout.emit.bind(child.stdout)
    child.stdout.emit = (event, ...values) => {
      if (event !== 'data') return emit(event, ...values)
      text += values[0].toString()
      let line
      while ((line = text.indexOf('\n')) >= 0) {
        const raw = text.slice(0, line)
        text = text.slice(line + 1)
        try {
          const frame = JSON.parse(raw)
          state.frames.push(frame)
          if (frame.kind === 'primary') {
            state.primary = frame
            if (onPrimary) {
              held = true
              Promise.resolve()
                .then(() => onPrimary(frame))
                .then(() => {
                  held = false
                  const queued = buffered
                  buffered = []
                  for (const data of queued) emit('data', ...data)
                })
                .catch((error) => {
                  state.error = error.message
                  child.kill()
                })
            }
          }
        } catch {
          /* Diagnostic non-JSON lines do not become protocol frames. */
        }
      }
      if (held) {
        buffered.push(values)
        return true
      }
      return emit(event, ...values)
    }
    return child
  }
  syncBuiltinESMExports()
  return state
}

export function sendActivationPipe(pipeName, payload) {
  return new Promise((resolve, reject) => {
    let socket,
      finished = false
    const timeout = setTimeout(() => {
      finished = true
      socket?.destroy()
      reject(Error('Activation pipe timed out'))
    }, 5000)
    const attempt = () => {
      if (finished) return
      socket = connect(`\\\\.\\pipe\\${pipeName}`)
      let received = Buffer.alloc(0),
        sent = false
      socket.on('error', (error) => {
        if (!sent && ['ENOENT', 'EBUSY'].includes(error.code)) {
          // Match NamedPipeClient.ConnectAsync while the server replaces its single listener.
          setTimeout(attempt, 10)
          return
        }
        finished = true
        clearTimeout(timeout)
        reject(error)
      })
      socket.on('data', (chunk) => {
        received = Buffer.concat([received, chunk])
        if (!sent && received.length >= 4) {
          sent = true
          socket.write(payload)
        }
        if (received.length >= 5) {
          finished = true
          clearTimeout(timeout)
          socket.end()
          resolve({ processId: received.readUInt32LE(0), accepted: received[4] === 1 })
        }
      })
    }
    attempt()
  })
}

export function gamePayload(id) {
  const value = Buffer.alloc(9)
  value[0] = 3
  value.writeBigInt64LE(BigInt(id), 1)
  return value
}

export function pluginPayload(uri) {
  const value = Buffer.from(uri, 'utf8'),
    header = Buffer.alloc(3)
  header[0] = 4
  header.writeUInt16LE(value.length, 1)
  return Buffer.concat([header, value])
}

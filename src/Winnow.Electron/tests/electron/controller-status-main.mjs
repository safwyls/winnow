import childProcess from 'node:child_process'
import { syncBuiltinESMExports } from 'node:module'
import { EventEmitter } from 'node:events'
import { PassThrough } from 'node:stream'

// Replace only the OS probe process. Real main IPC validation, parsing, device matching,
// preload, renderer and backend remain in the test path.
globalThis.__controllerReadings = []
globalThis.__controllerStarts = 0
globalThis.__controllerStops = 0
const originalSpawn = childProcess.spawn
childProcess.spawn = function (file, args, options) {
  const encoded = args?.indexOf('-EncodedCommand') ?? -1
  if (
    encoded < 0 ||
    !Buffer.from(args[encoded + 1], 'base64')
      .toString('utf16le')
      .includes('WinnowControllerProbe')
  )
    return originalSpawn.apply(this, arguments)
  globalThis.__controllerStarts++
  const child = Object.assign(new EventEmitter(), {
    stdout: new PassThrough(),
    stderr: new PassThrough(),
    stdin: new PassThrough(),
  })
  let killed = false
  child.kill = () => {
    if (!killed) {
      killed = true
      globalThis.__controllerStops++
      child.emit('exit', 0)
    }
    return true
  }
  child.stdin.on('data', () =>
    queueMicrotask(() => {
      if (!killed) child.stdout.write(JSON.stringify(globalThis.__controllerReadings) + '\n')
    }),
  )
  return child
}
syncBuiltinESMExports()
await import('../../out/main/index.js')

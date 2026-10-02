import { app } from 'electron'
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'

const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
const acquire = app.requestSingleInstanceLock.bind(app)
app.requestSingleInstanceLock = (data) => {
  writeFileSync(join(directory, 'secondary-requesting.json'), JSON.stringify({ activation: data.activation }))
  return acquire(data)
}
await import('../../out/main/index.js')

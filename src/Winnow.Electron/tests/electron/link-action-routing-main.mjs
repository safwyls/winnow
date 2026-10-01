import { app } from 'electron'

// Match the original router row's available Steam client without consulting the host registry.
app.getApplicationNameForProtocol = (address) => (address === 'steam://' ? 'Steam fixture' : '')
await import('./plugin-game-actions-main.mjs')

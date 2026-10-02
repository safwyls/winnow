import { defineConfig, externalizeDepsPlugin } from 'electron-vite'
import react from '@vitejs/plugin-react'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { buildInformationalVersion } from './build/application-metadata'
export default defineConfig({
  main: {
    plugins: [externalizeDepsPlugin()],
    define: {
      __WINNOW_BUILD_INFORMATIONAL_VERSION__: JSON.stringify(
        buildInformationalVersion(fileURLToPath(new URL('.', import.meta.url))),
      ),
    },
  },
  preload: {
    plugins: [externalizeDepsPlugin()],
    build: {
      rollupOptions: {
        input: { index: resolve('src/preload/index.ts'), epic: resolve('src/preload/epic.ts') },
        output: { format: 'cjs', entryFileNames: '[name].cjs' },
      },
    },
  },
  renderer: { plugins: [react()], server: { host: '127.0.0.1' } },
})

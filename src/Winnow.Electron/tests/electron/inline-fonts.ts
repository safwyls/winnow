import type { Plugin } from 'esbuild'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

/** Match Vite's inline font imports in native main-process fixture bundles. */
export function inlineFonts(): Plugin {
  return {
    name: 'real-inline-fonts',
    setup(bundle) {
      bundle.onResolve({ filter: /\.ttf\?inline$/ }, (args) => ({
        path: resolve(args.resolveDir, args.path.replace(/\?inline$/, '')),
        namespace: 'inline-font',
      }))
      bundle.onLoad({ filter: /.*/, namespace: 'inline-font' }, async (args) => ({
        contents: await readFile(args.path),
        loader: 'dataurl',
      }))
    },
  }
}

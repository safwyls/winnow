import { describe, expect, it } from 'vitest'
import { parse } from '@babel/parser'
import { build } from 'esbuild'
import { builtinModules } from 'node:module'
import { readdir, readFile } from 'node:fs/promises'
import { dirname, relative, resolve } from 'node:path'

const sourceRoot = resolve('src'),
  rendererRoot = resolve('src/renderer'),
  sharedRoot = resolve('src/shared'),
  nativeModules = new Set(builtinModules.flatMap((name) => [name, `node:${name}`]))
type Edge = { module: string; typeOnly: boolean }
function imports(source: string): Edge[] {
  const result: Edge[] = []
  const ast = parse(source, { sourceType: 'module', plugins: ['typescript', 'jsx'] })
  function visit(value: unknown) {
    if (!value || typeof value !== 'object') return
    if (Array.isArray(value)) {
      value.forEach(visit)
      return
    }
    const node = value as Record<string, any>
    if (typeof node.type !== 'string') return
    if (
      ['ImportDeclaration', 'ExportNamedDeclaration', 'ExportAllDeclaration'].includes(node.type) &&
      node.source
    ) {
      result.push({
        module: node.source.value,
        typeOnly:
          node.importKind === 'type' ||
          node.exportKind === 'type' ||
          (node.specifiers?.length > 0 &&
            node.specifiers.every((item: any) => item.importKind === 'type' || item.exportKind === 'type')),
      })
    } else if (node.type === 'TSImportType') {
      result.push({ module: node.argument.value, typeOnly: true })
    } else if (node.type === 'TSExternalModuleReference') {
      result.push({ module: node.expression.value, typeOnly: false })
    } else if (node.type === 'ImportExpression' && node.source?.type === 'StringLiteral') {
      result.push({ module: node.source.value, typeOnly: false })
    } else if (
      node.type === 'CallExpression' &&
      (node.callee?.type === 'Import' ||
        (node.callee?.type === 'Identifier' && node.callee.name === 'require')) &&
      node.arguments[0]?.type === 'StringLiteral'
    ) {
      result.push({ module: node.arguments[0].value, typeOnly: false })
    }
    Object.values(node).forEach(visit)
  }
  visit(ast.program)
  return result
}
function inside(root: string, file: string) {
  const path = relative(root, file)
  return path !== '..' && !path.startsWith('..\\') && !path.startsWith('../') && !/^[A-Za-z]:/.test(path)
}
function forbiddenRendererDependency(file: string, edge: Edge) {
  if (edge.module.startsWith('.')) {
    const target = resolve(dirname(file), edge.module.split('?')[0])
    return !inside(rendererRoot, target) && !inside(sharedRoot, target)
  }
  return (
    /^electron(?:\/|$)/.test(edge.module) ||
    nativeModules.has(edge.module) ||
    edge.module.startsWith('node:') ||
    /(?:sqlite|Winnow\.(?:Data|Ingest|Enrich|Application|Backend|Monitor|Resolve|Plugins|Recommend|Covers)|@winnow\/(?:data|ingest|enrich|backend|monitor|resolve|plugins|recommend|covers))/i.test(
      edge.module,
    )
  )
}
async function sources(root: string): Promise<string[]> {
  const entries = await readdir(root, { withFileTypes: true })
  return (
    await Promise.all(
      entries.map((entry) =>
        entry.isDirectory()
          ? sources(resolve(root, entry.name))
          : /\.tsx?$/.test(entry.name)
            ? [resolve(root, entry.name)]
            : [],
      ),
    )
  ).flat()
}
async function graph(entryPoints: string[], platform: 'browser' | 'node') {
  const result = await build({
    entryPoints,
    bundle: true,
    write: false,
    metafile: true,
    platform,
    format: 'esm',
    outdir: resolve('../../.tmp/architecture-memory-only'),
    logLevel: 'silent',
    external: platform === 'node' ? ['electron'] : [],
    loader: { '.svg': 'dataurl', '.png': 'dataurl', '.ttf': 'file', '.woff': 'file', '.woff2': 'file' },
    plugins: [
      {
        name: 'source-svg',
        setup(builder) {
          builder.onResolve({ filter: /\?raw$/ }, (args) => ({
            path: resolve(args.resolveDir, args.path.slice(0, -4)),
            namespace: 'raw-source',
          }))
          builder.onLoad({ filter: /.*/, namespace: 'raw-source' }, async (args) => ({
            contents: await readFile(args.path, 'utf8'),
            loader: 'text',
          }))
        },
      },
    ],
  })
  return Object.keys(result.metafile!.inputs).map((file) => resolve(file))
}

describe('frontend module boundaries', () => {
  it.each([
    "import { readFile } from 'node:fs/promises'",
    "import type { BackendTransport } from '../main/transport'",
    "type Store = import('../main/transport').BackendTransport",
    "export { BackendTransport } from '../main/transport'",
    "const module = import('electron')",
    "import { ipcRenderer } from 'electron/renderer'",
    "const module = require('better-sqlite3')",
    "import { x } from '../../../Winnow.Ingest.Steam/reader'",
    "export type { Resolver } from '@winnow/resolve'",
    "import { readFile } from 'node:\\u0066s'",
  ])('rejects the forbidden dependency in %s', (source) => {
    expect(
      imports(source).some((edge) => forbiddenRendererDependency(resolve('src/renderer/example.ts'), edge)),
    ).toBe(true)
  })
  it('ignores comments and ordinary strings that describe an import', () => {
    expect(
      imports(
        "// import { x } from 'node:fs'\nconst help = \"import('electron')\"; import type { ApiRequest } from '../shared/bridge'",
      ),
    ).toEqual([{ module: '../shared/bridge', typeOnly: true }])
  })
  it('views and their shared types cannot import native or backend implementations', async () => {
    const files = [...(await sources(rendererRoot)), ...(await sources(sharedRoot))],
      offenders: string[] = []
    expect(files.length).toBeGreaterThan(0)
    for (const file of files)
      for (const edge of imports(await readFile(file, 'utf8')))
        if (forbiddenRendererDependency(file, edge))
          offenders.push(`${relative(sourceRoot, file)} -> ${edge.module}`)
    expect(offenders).toEqual([])
  })
  it('the compiled renderer and drawing worker dependency graphs stay in presentation and shared contracts', async () => {
    const files = await graph(
      ['src/renderer/src/main.tsx', 'src/renderer/startup/dragon.worker.ts'],
      'browser',
    )
    expect(files.some((file) => file.endsWith('App.tsx'))).toBe(true)
    expect(files.some((file) => file.endsWith('dragon.worker.ts'))).toBe(true)
    const local = files.filter((file) => inside(sourceRoot, file))
    expect(local.filter((file) => !inside(rendererRoot, file) && !inside(sharedRoot, file))).toEqual([])
    expect(
      files.filter((file) =>
        /Winnow\.(?:Data|Ingest|Enrich|Application|Backend|Monitor|Resolve|Plugins|Recommend|Covers)[\\/]/i.test(
          file,
        ),
      ),
    ).toEqual([])
  })
  it('embedded sign-in hosts compile against frontend contracts without backend repository or transport implementations', async () => {
    const files = await graph(['src/main/steam-auth.ts', 'src/main/epic-auth.ts'], 'node')
    expect(files.some((file) => file.endsWith('steam-auth.ts'))).toBe(true)
    expect(files.some((file) => file.endsWith('epic-auth.ts'))).toBe(true)
    expect(
      files.filter((file) =>
        /Winnow\.(?:Data|Ingest|Enrich|Application|Backend|Monitor|Resolve|Plugins|Recommend|Covers)[\\/]|sqlite/i.test(
          file,
        ),
      ),
    ).toEqual([])
    expect(files.filter((file) => file === resolve('src/main/transport.ts'))).toEqual([])
  })
})

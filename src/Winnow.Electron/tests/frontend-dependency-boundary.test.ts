import { readFileSync, existsSync } from 'node:fs'
import { builtinModules } from 'node:module'
import { dirname, extname, relative, resolve, sep } from 'node:path'
import { parse } from '@babel/parser'
import { describe, expect, it } from 'vitest'

type Node = { type: string; [key: string]: unknown }
type Edge = { from: string; specifier: string; to?: string }
const root = resolve(import.meta.dirname, '..')
const production = resolve(root, 'src')
const renderer = resolve(production, 'renderer')
const shared = resolve(production, 'shared')
const main = resolve(production, 'main')
const packageManifest = resolve(root, 'package.json')
const inside = (file: string, directory: string) => file === directory || file.startsWith(directory + sep)
const backend =
  /(?:^|[/\\])Winnow\.(?:Backend|Data|Application|Ingest|Enrich|Monitor|Plugins|Resolve|Recommend)(?:[./\\]|$)/i
const storagePackages = /^(?:better-sqlite3|sqlite3?|sql\.js|@libsql\/client|node:sqlite)(?:\/|$)/
const builtins = new Set(builtinModules.map((name) => name.replace(/^node:/, '')))
const manifest = JSON.parse(readFileSync(packageManifest, 'utf8'))
const packageName = (specifier: string) =>
  specifier
    .split('/')
    .slice(0, specifier.startsWith('@') ? 2 : 1)
    .join('/')

function walk(value: unknown, visit: (node: Node) => void) {
  if (!value || typeof value !== 'object') return
  if (Array.isArray(value)) {
    value.forEach((item) => walk(item, visit))
    return
  }
  const node = value as Node
  if (typeof node.type === 'string') visit(node)
  Object.values(value).forEach((child) => walk(child, visit))
}
function literal(value: unknown): string | undefined {
  const node = value as Node | undefined
  return node?.type === 'StringLiteral' ? (node.value as string) : undefined
}
function localTarget(from: string, specifier: string) {
  const base = resolve(dirname(from), specifier.split('?')[0])
  const candidates = [
    base,
    ...['.ts', '.tsx', '.js', '.jsx', '.json', '/index.ts', '/index.tsx'].map((suffix) => base + suffix),
  ]
  if (/\.jsx?$/.test(base)) candidates.push(base.replace(/\.js$/, '.ts'), base.replace(/\.jsx?$/, '.tsx'))
  const target = candidates.find((candidate) => existsSync(candidate) && extname(candidate))
  expect(target, `Unresolved production import ${relative(root, from)} → ${specifier}`).toBeDefined()
  return target!
}
function graph(entry: string) {
  const visited = new Set<string>(),
    edges: Edge[] = [],
    dynamic: string[] = [],
    transports: string[] = [],
    pending = [resolve(root, entry)]
  while (pending.length) {
    const file = pending.pop()!
    if (visited.has(file)) continue
    visited.add(file)
    if (!/\.[cm]?[jt]sx?$/.test(file)) continue
    const tree = parse(readFileSync(file, 'utf8'), {
      sourceType: 'module',
      plugins: ['typescript', 'jsx'],
      createImportExpressions: true,
    })
    const imports = new Set<string>()
    walk(tree, (node) => {
      if (
        ['ImportDeclaration', 'ExportNamedDeclaration', 'ExportAllDeclaration', 'ImportExpression'].includes(
          node.type,
        )
      ) {
        const specifier = literal(node.source)
        if (specifier) imports.add(specifier)
        else if (node.type === 'ImportExpression') dynamic.push(relative(root, file).replaceAll('\\', '/'))
      }
      const callee = node.callee as Node | undefined
      const called =
        callee?.type === 'Identifier'
          ? callee.name
          : callee?.type === 'MemberExpression' &&
              ['window', 'globalThis', 'self'].includes(String((callee.object as Node)?.name))
            ? (callee.property as Node)?.name
            : undefined
      if (
        ['CallExpression', 'NewExpression'].includes(node.type) &&
        ['fetch', 'XMLHttpRequest', 'WebSocket', 'EventSource'].includes(String(called))
      )
        transports.push(`${relative(root, file)} → ${called}`)
      if (node.type === 'CallExpression' && callee?.type === 'Identifier' && callee.name === 'require') {
        const specifier = literal((node.arguments as unknown[])[0])
        if (specifier) imports.add(specifier)
        else dynamic.push(relative(root, file).replaceAll('\\', '/'))
      }
      // Vite worker entries are outside normal static imports and must obey the same boundary.
      if (node.type === 'NewExpression' && callee?.type === 'Identifier' && callee.name === 'URL') {
        const [first, second] = node.arguments as Node[]
        if ((second?.object as Node)?.type === 'MetaProperty') {
          const specifier = literal(first)
          if (specifier?.startsWith('.')) imports.add(specifier)
        }
      }
      if (node.type === 'TSImportType') {
        const specifier = literal(node.argument)
        if (specifier) imports.add(specifier)
      }
    })
    for (const specifier of imports) {
      const to = specifier.startsWith('.') ? localTarget(file, specifier) : undefined
      edges.push({ from: file, specifier, to })
      if (to) pending.push(to)
    }
  }
  return { visited, edges, dynamic, transports }
}

describe('FrontendDependencyGraphContainsNoBackendImplementationAssemblies', () => {
  it('recursively keeps renderer, main and preload imports outside backend implementation and database packages', () => {
    const graphs = [
      graph(
        `src/renderer${readFileSync(resolve(renderer, 'index.html'), 'utf8').match(/<script\s+type="module"\s+src="([^"]+)"/)![1]}`,
      ),
      graph('src/main/index.ts'),
      graph('src/preload/index.ts'),
      graph('src/preload/epic.ts'),
    ]
    // Guard the traversal itself: these transitive modules are not imported by the entry points directly.
    expect(graphs[0].visited.has(resolve(renderer, 'features/ManualEditor.tsx'))).toBe(true)
    expect(graphs[0].visited.has(resolve(renderer, 'startup/dragon.worker.ts'))).toBe(true)
    expect(graphs[1].visited.has(resolve(production, 'main/backend-service.ts'))).toBe(true)
    expect(graphs[1].visited.has(packageManifest)).toBe(true)
    for (const { edges } of graphs)
      for (const edge of edges) {
        const identity = `${relative(root, edge.from)} → ${edge.specifier}`
        expect(backend.test(edge.specifier) || (edge.to ? backend.test(edge.to) : false), identity).toBe(
          false,
        )
        expect(storagePackages.test(edge.specifier), identity).toBe(false)
        if (edge.to)
          // Main may read its own package identity; other files outside src remain forbidden.
          expect(
            inside(edge.to, production) || (edge.to === packageManifest && inside(edge.from, main)),
            identity,
          ).toBe(true)
        else
          expect(
            edge.specifier === 'electron' ||
              builtins.has(edge.specifier.replace(/^node:/, '')) ||
              packageName(edge.specifier) in manifest.dependencies,
            identity,
          ).toBe(true)
      }
    for (const edge of graphs[0].edges) {
      const identity = `${relative(root, edge.from)} → ${edge.specifier}`
      if (edge.to) expect(inside(edge.to, renderer) || inside(edge.to, shared), identity).toBe(true)
      else
        expect(
          edge.specifier === 'electron' || edge.specifier.startsWith('node:') || builtins.has(edge.specifier),
          identity,
        ).toBe(false)
    }
    // Developer themes are intentionally loaded by URL after scheme/origin validation. No other
    // computed import may bypass the graph; the browser remains sandboxed for authored themes too.
    expect(graphs[0].dynamic).toEqual(['src/renderer/theming/runtime.tsx'])
    expect(graphs.slice(1).flatMap(({ dynamic }) => dynamic)).toEqual([])
    expect(graphs[0].transports, 'Renderer transport must use the named preload bridge').toEqual([])
  })

  it('ships the backend as a separate resource and exposes only the named preload bridge to a sandboxed renderer', () => {
    expect(manifest.build.files).toEqual(['out/**/*', 'package.json'])
    expect(manifest.build.extraResources).toContainEqual({ from: '.staging/backend', to: 'backend' })
    for (const dependency of Object.keys(manifest.dependencies)) {
      expect(backend.test(dependency) || storagePackages.test(dependency)).toBe(false)
    }
    const preload = readFileSync(resolve(production, 'preload/index.ts'), 'utf8')
    expect(preload).toContain("contextBridge.exposeInMainWorld('winnow', Object.freeze(bridge))")
    const source = readFileSync(resolve(production, 'main/index.ts'), 'utf8')
    const tree = parse(source, { sourceType: 'module', plugins: ['typescript'] })
    const windows: Node[] = []
    walk(tree, (node) => {
      if (node.type === 'NewExpression' && (node.callee as Node)?.name === 'BrowserWindow') windows.push(node)
    })
    expect(windows.length).toBeGreaterThan(0)
    for (const window of windows) {
      const config = (window.arguments as Node[])[0]
      const properties = config.properties as Node[]
      const webPreferences = properties.find((property) => (property.key as Node)?.name === 'webPreferences')
        ?.value as Node
      const flags = Object.fromEntries(
        (webPreferences.properties as Node[]).map((property) => [
          (property.key as Node)?.name,
          (property.value as Node)?.value,
        ]),
      )
      expect(flags).toMatchObject({ sandbox: true, contextIsolation: true, nodeIntegration: false })
    }
  })
})

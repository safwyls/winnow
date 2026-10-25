import { parse } from '@babel/parser'
import type { Node } from '@babel/types'
import { readFileSync, readdirSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

const renderer = join(import.meta.dirname, '../src/renderer')
const nativeDropdowns = new Set(['select', 'datalist'])

function sourceFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) return sourceFiles(path)
    return /\.[cm]?[jt]sx?$/.test(entry.name) ? [path] : []
  })
}

function walk(node: Node, inspect: (node: Node) => void) {
  inspect(node)
  for (const value of Object.values(node)) {
    const children = Array.isArray(value) ? value : [value]
    for (const child of children)
      if (child && typeof child === 'object' && typeof child.type === 'string') walk(child as Node, inspect)
  }
}

function staticString(node: Node | null | undefined): string | undefined {
  if (node?.type === 'StringLiteral') return node.value
  if (node?.type === 'TemplateLiteral' && !node.expressions.length)
    return node.quasis[0]?.value.cooked ?? undefined
  return undefined
}

function nativeDropdownFailures(source: string, file: string): string[] {
  const tree = parse(source, { sourceType: 'module', plugins: ['typescript', 'jsx'] })
  const factories = new Set(['createElement'])
  walk(tree, (node) => {
    if (node.type !== 'ImportDeclaration' || node.source.value !== 'react') return
    for (const specifier of node.specifiers)
      if (
        specifier.type === 'ImportSpecifier' &&
        (specifier.imported.type === 'Identifier' ? specifier.imported.name : specifier.imported.value) ===
          'createElement'
      )
        factories.add(specifier.local.name)
  })
  const failures: string[] = []
  walk(tree, (node) => {
    let tag: string | undefined
    if (node.type === 'JSXOpeningElement' && node.name.type === 'JSXIdentifier') tag = node.name.name
    else if (node.type === 'CallExpression' || node.type === 'OptionalCallExpression') {
      const callee = node.callee
      const isFactory =
        (callee.type === 'Identifier' && factories.has(callee.name)) ||
        ((callee.type === 'MemberExpression' || callee.type === 'OptionalMemberExpression') &&
          (callee.computed
            ? staticString(callee.property)
            : callee.property.type === 'Identifier'
              ? callee.property.name
              : undefined) === 'createElement')
      if (isFactory) tag = staticString(node.arguments[0])?.toLowerCase()
    }
    if (tag && nativeDropdowns.has(tag))
      failures.push(
        `${file}:${node.loc?.start.line ?? 1}:${(node.loc?.start.column ?? 0) + 1} Native <${tag}> bypasses the themed ChoiceMenu.`,
      )
  })
  return failures
}

describe('production dropdown rendering', () => {
  it('uses themed controls instead of native select or datalist anywhere in the renderer', () => {
    const files = sourceFiles(renderer)
    expect(files.length).toBeGreaterThan(0)
    const failures = files.flatMap((file) =>
      nativeDropdownFailures(readFileSync(file, 'utf8'), relative(renderer, file).replaceAll('\\', '/')),
    )
    expect(failures, `Replace native dropdowns with ChoiceMenu:\n${failures.join('\n')}`).toEqual([])
  })

  it('detects intrinsic JSX, React/DOM factories and imported aliases with source locations', () => {
    const source = [
      "import { createElement as element } from 'react'",
      'const first = <select><option>One</option></select>',
      'const second = <datalist />',
      "createElement('select')",
      "React.createElement('datalist')",
      "document.createElement('SELECT')",
      "React['createElement'](`datalist`)",
      "element('select')",
    ].join('\n')
    const failures = nativeDropdownFailures(source, 'fixture.tsx')
    expect(failures).toHaveLength(7)
    expect(failures.map((failure) => failure.split(' ')[0])).toEqual([
      'fixture.tsx:2:15',
      'fixture.tsx:3:16',
      'fixture.tsx:4:1',
      'fixture.tsx:5:1',
      'fixture.tsx:6:1',
      'fixture.tsx:7:1',
      'fixture.tsx:8:1',
    ])
    expect(failures.every((failure) => failure.includes('the themed ChoiceMenu'))).toBe(true)
  })

  it('ignores comments, quoted documentation, custom components and ordinary elements', () => {
    const source = [
      '// <select /> and document.createElement("datalist") are forbidden in production.',
      'const documentation = "<select><option>One</option></select>"',
      'const custom = <ChoiceMenu label="Store" value="" options={[]} onChange={() => {}} />',
      'const other = <Select />',
      'React.createElement(ChoiceMenu)',
      "document.createElement('button')",
      'const input = <input aria-label="Search" />',
    ].join('\n')
    expect(nativeDropdownFailures(source, 'fixture.tsx')).toEqual([])
  })
})

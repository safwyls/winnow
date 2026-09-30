import { readFileSync } from 'node:fs'
import { parse } from '@babel/parser'
import { expect, it } from 'vitest'

it('static merge markup reads its visible text and accessible copy through bindings', () => {
  const literals: string[] = []
  function visit(node: unknown) {
    if (!node || typeof node !== 'object') return
    const item = node as Record<string, any>
    if (item.type === 'JSXText' && item.value.trim()) literals.push(item.value.trim())
    if (
      item.type === 'JSXAttribute' &&
      ['title', 'aria-label', 'placeholder'].includes(item.name.name) &&
      item.value?.type === 'StringLiteral'
    )
      literals.push(item.value.value)
    for (const child of Object.values(item)) {
      if (Array.isArray(child)) child.forEach(visit)
      else if (child && typeof child === 'object') visit(child)
    }
  }
  visit(
    parse(readFileSync(new URL('../src/renderer/features/parity-merge.tsx', import.meta.url), 'utf8'), {
      sourceType: 'module',
      plugins: ['typescript', 'jsx'],
    }),
  )
  expect(literals).toEqual([])
})

it('queue and receipt copy use ordinary answers without retract merge records or cancel', () => {
  const copy: string[] = []
  function collect(node: unknown) {
    if (!node || typeof node !== 'object') return
    const value = node as Record<string, unknown>
    if (value.type === 'StringLiteral') copy.push(String(value.value))
    if (value.type === 'JSXText') copy.push(String(value.value).replace(/\s+/g, ' ').trim())
    if (value.type === 'TemplateElement') copy.push(String((value.value as { cooked: string }).cooked))
    for (const child of Object.values(value)) {
      if (Array.isArray(child)) child.forEach(collect)
      else if (child && typeof child === 'object') collect(child)
    }
  }
  // Confirmation sheets have a real Cancel action; it must never replace a queue verdict.
  for (const file of [
    'parity-merge.tsx',
    'parity-merge-copy.ts',
    'parity-merge-model.ts',
    'parity-merge-facts.ts',
  ]) {
    collect(
      parse(readFileSync(new URL(`../src/renderer/features/${file}`, import.meta.url), 'utf8'), {
        sourceType: 'module',
        plugins: ['typescript', 'jsx'],
      }),
    )
  }
  expect(copy).toContain('Same game')
  expect(copy).toContain('Different games')
  expect(copy).toContain('Undo review decisions')
  for (const text of copy) expect(text).not.toMatch(/\bretract\b|merge records|\bcancel\b/i)
})

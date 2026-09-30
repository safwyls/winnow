import { readFileSync } from 'node:fs'
import { parse } from '@babel/parser'
import { expect, it } from 'vitest'

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

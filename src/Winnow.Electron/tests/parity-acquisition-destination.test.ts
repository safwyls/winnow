import { afterEach, expect, it, vi } from 'vitest'
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { saveAcquisitions } from '../src/main/acquisition-export'

const roots: string[] = []
afterEach(async () => {
  for (const root of roots.splice(0)) await rm(root, { recursive: true, force: true })
})
async function fixture() {
  const root = await mkdtemp(join(tmpdir(), 'winnow-acquisition-export-'))
  roots.push(root)
  return root
}

it('source cancelled destination returns false and leaves the existing CSV and directory untouched', async () => {
  const root = await fixture()
  const existing = join(root, 'keep.csv')
  await writeFile(existing, 'existing content')
  const choose = vi.fn(async () => null)
  await expect(saveAcquisitions('never written', choose)).resolves.toBe(false)
  expect(choose).toHaveBeenCalledOnce()
  expect(await readdir(root)).toEqual(['keep.csv'])
  expect(await readFile(existing, 'utf8')).toBe('existing content')
})

it('source accepted destination preserves the default filename CRLF accented text and UTF-8 BOM bytes', async () => {
  const root = await fixture()
  const path = join(root, 'chosen.csv')
  const csv = 'title,currency\r\nÉlan,EUR\r\n'
  const choose = vi.fn(async () => path)
  await expect(saveAcquisitions(csv, choose)).resolves.toBe(true)
  expect(choose).toHaveBeenCalledExactlyOnceWith({
    title: 'Export acquisitions',
    mode: 'save',
    suggestedName: 'winnow-acquisitions.csv',
    filterName: 'CSV',
    extensions: ['csv'],
  })
  const bytes = await readFile(path)
  expect([...bytes.subarray(0, 3)]).toEqual([0xef, 0xbb, 0xbf])
  expect(bytes.subarray(3).toString('utf8')).toBe(csv)
  expect(await readdir(root)).toEqual(['chosen.csv'])
})

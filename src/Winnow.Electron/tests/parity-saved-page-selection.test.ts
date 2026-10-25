import { afterEach, expect, it, vi } from 'vitest'
import { mkdtemp, open, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { SavedSteamPages } from '../src/main/saved-steam-pages'

const roots: string[] = []
afterEach(async () => {
  for (const root of roots.splice(0)) await rm(root, { recursive: true, force: true })
})
async function fixture() {
  const root = await mkdtemp(join(tmpdir(), 'winnow-saved-pages-'))
  roots.push(root)
  return root
}

it('chooses opaque deduplicated HTML handles without opening contents until Read', async () => {
  const path = join(await fixture(), 'licenses.HTML')
  const choose = vi.fn(async () => path)
  const pages = new SavedSteamPages(choose, vi.fn())
  const selected = (await pages.choose())!
  expect(selected).toEqual({ id: expect.any(String), name: 'licenses.HTML' })
  expect(selected.id).not.toContain(path)
  expect(await pages.choose()).toEqual(selected)
  expect(choose).toHaveBeenCalledWith(expect.objectContaining({ mode: 'open', extensions: ['html', 'htm'] }))
  // A nonexistent path can be selected; content is created only before explicit Read.
  const html = '<table>Élan</table>\r\n'
  await writeFile(path, html)
  expect(await pages.read([selected.id, selected.id])).toEqual([
    { name: 'licenses.HTML', content: Buffer.from(html).toString('base64') },
  ])
})

it('cancel revokes handles and an outstanding chooser cannot restore a disposed selection', async () => {
  let finish!: (path: string | null) => void
  const cancel = vi.fn()
  const pages = new SavedSteamPages(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }),
    cancel,
  )
  const pending = pages.choose()
  pages.clear()
  expect(cancel).toHaveBeenCalledOnce()
  finish('C:\\fixture\\old.html')
  expect(await pending).toBeNull()
  const next = pages.choose()
  finish('C:\\fixture\\new.htm')
  const selected = (await next)!
  pages.clear()
  await expect(pages.read([selected.id])).rejects.toThrow('Choose the saved pages again')
  await expect(pages.read(['C:\\fixture\\new.htm'])).rejects.toThrow('Choose the saved pages again')
})

it('rejects non-HTML selections and reports missing pages without exposing directory paths', async () => {
  const root = await fixture()
  const choose = vi
    .fn()
    .mockResolvedValueOnce(join(root, 'secret.txt'))
    .mockResolvedValueOnce(join(root, 'gone.htm'))
    .mockResolvedValueOnce(null)
  const pages = new SavedSteamPages(choose, vi.fn())
  await expect(pages.choose()).rejects.toThrow('Choose an HTML page')
  const selected = (await pages.choose())!
  await expect(pages.read([selected.id])).rejects.toThrow(
    'Could not read gone.htm. Choose the page again and retry.',
  )
  expect(await pages.choose()).toBeNull()
})

it('leaving during Read aborts the result and a fresh selection can be read afterward', async () => {
  const path = join(await fixture(), 'licenses.html')
  await writeFile(path, '<html>licenses</html>')
  const pages = new SavedSteamPages(async () => path, vi.fn())
  const selected = (await pages.choose())!
  const pending = pages.read([selected.id])
  pages.clear()
  await expect(pending).rejects.toMatchObject({ name: 'AbortError' })
  const fresh = (await pages.choose())!
  expect(fresh.id).not.toBe(selected.id)
  expect(await pages.read([fresh.id])).toEqual([
    { name: 'licenses.html', content: Buffer.from('<html>licenses</html>').toString('base64') },
  ])
})

it('enforces individual and combined size limits before allocating page contents', async () => {
  const root = await fixture()
  const paths = [join(root, 'large.html'), ...[1, 2, 3].map((id) => join(root, `${id}.html`))]
  for (const [index, path] of paths.entries()) {
    const handle = await open(path, 'w')
    try {
      await handle.truncate((index === 0 ? 65 : 48) * 1024 * 1024)
    } finally {
      await handle.close()
    }
  }
  const choose = vi.fn(async () => paths.shift()!)
  const pages = new SavedSteamPages(choose, vi.fn())
  const large = (await pages.choose())!
  await expect(pages.read([large.id])).rejects.toThrow('64 MB')
  const selected = await Promise.all([pages.choose()])
  selected.push(await pages.choose(), await pages.choose())
  await expect(pages.read(selected.map((file) => file!.id))).rejects.toThrow('128 MB')
})

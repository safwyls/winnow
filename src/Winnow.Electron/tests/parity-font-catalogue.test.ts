import { describe, expect, it } from 'vitest'
import { FontCatalogue, fontFamilies } from '../src/main/fonts'
import { trustedRendererUrl } from '../src/main/security'

describe('installed font catalogue boundary', () => {
  it('recognizes the registered renderer scheme without trusting opaque or different origins', async () => {
    const catalogue = new FontCatalogue(trustedRendererUrl)
    const reader = {
      getURL: () => 'winnow-app://app/index.html',
      isDestroyed: () => false,
      executeJavaScript: async () => {
        expect(catalogue.allows(reader, 'local-fonts', 'winnow-app://app', true)).toBe(true)
        expect(catalogue.allows(reader, 'local-fonts', 'winnow-app://app/', true)).toBe(true)
        expect(catalogue.allows(reader, 'local-fonts', 'null', true)).toBe(false)
        expect(catalogue.allows(reader, 'local-fonts', 'winnow-app://other', true)).toBe(false)
        expect(catalogue.allows(reader, 'local-fonts', 'winnow-app://app', false)).toBe(false)
        return ['Segoe UI']
      },
    }
    expect(await catalogue.read(reader)).toEqual(['Segoe UI'])
  })
  it('returns distinct family names and excludes control characters or CSS content', () => {
    expect(
      fontFamilies(['Calibri', 'Arial', 'Arial', ' Bad; rule{} ', '', '\u0000invalid', 42, '  Consolas  ']),
    ).toEqual(['Arial', 'Calibri', 'Consolas'])
  })
  it('grants only a pending trusted top-level font request and revokes after success', async () => {
    const catalogue = new FontCatalogue(
      (url) => url === 'https://winnow.test/' || url === 'https://winnow.test',
    )
    const reader = {
      getURL: () => 'https://winnow.test/',
      isDestroyed: () => false,
      executeJavaScript: async () => {
        expect(catalogue.allows(reader, 'local-fonts', 'https://winnow.test', true)).toBe(true)
        expect(catalogue.allows(reader, 'camera', 'https://winnow.test', true)).toBe(false)
        expect(catalogue.allows(reader, 'local-fonts', 'https://external.test', true)).toBe(false)
        expect(catalogue.allows(reader, 'local-fonts', 'https://winnow.test', false)).toBe(false)
        return ['Calibri']
      },
    }
    expect(catalogue.allows(reader, 'local-fonts', 'https://winnow.test', true)).toBe(false)
    expect(await catalogue.read(reader)).toEqual(['Calibri'])
    expect(catalogue.allows(reader, 'local-fonts', 'https://winnow.test', true)).toBe(false)
  })
  it('revokes permission and discards a result after navigation', async () => {
    let url = 'https://winnow.test/'
    const catalogue = new FontCatalogue((value) => value === url)
    const reader = {
      getURL: () => url,
      isDestroyed: () => false,
      executeJavaScript: async () => {
        url = 'https://other.test'
        return ['Calibri']
      },
    }
    await expect(catalogue.read(reader)).rejects.toThrow('window changed')
    expect(catalogue.allows(reader, 'local-fonts', url, true)).toBe(false)
  })
})

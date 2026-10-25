import { readFile, stat } from 'node:fs/promises'
import type { ApiResult, ArtworkImport, ArtworkSaveResult } from '../shared/bridge'

export const artworkFileLimit = 16 * 1024 * 1024
export function validArtworkImport(input: unknown): input is ArtworkImport {
  if (!input || typeof input !== 'object') return false
  const value = input as ArtworkImport
  return (
    Number.isSafeInteger(value.workId) &&
    value.workId > 0 &&
    ['Hero', 'Cover', 'Icon'].includes(value.slot) &&
    typeof value.revision === 'string' &&
    /^[A-Fa-f0-9]{64}$/.test(value.revision)
  )
}
export async function readArtworkFile(path: string) {
  const info = await stat(path)
  if (!info.isFile()) throw new Error('Choose an image file.')
  if (info.size > artworkFileLimit) throw new Error('Choose an image no larger than 16 MiB.')
  const bytes = await readFile(path)
  if (bytes.length > artworkFileLimit) throw new Error('Choose an image no larger than 16 MiB.')
  return bytes
}

/** Only a main-process picker supplies a path; the renderer names the target slot. */
export async function importArtworkFile(
  input: unknown,
  dependencies: {
    choose(): Promise<string | null>
    read?(path: string): Promise<Uint8Array>
    upload(input: ArtworkImport, bytes: Uint8Array): Promise<ApiResult<ArtworkSaveResult>>
  },
): Promise<ApiResult<ArtworkSaveResult> | null> {
  if (!validArtworkImport(input))
    return { ok: false, status: 400, message: 'Refresh the current artwork before importing.' }
  const path = await dependencies.choose()
  if (!path) return null
  const bytes = await (dependencies.read ?? readArtworkFile)(path)
  if (!bytes.length || bytes.length > artworkFileLimit)
    return { ok: false, status: 400, message: 'Choose a non-empty image no larger than 16 MiB.' }
  return dependencies.upload(input, bytes)
}

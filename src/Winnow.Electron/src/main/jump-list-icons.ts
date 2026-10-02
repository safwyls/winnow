import { createHash, randomUUID } from 'node:crypto'
import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import type { NativeImage } from 'electron'
import type { CoverKey } from '../renderer/api/types'

export const jumpListIconSizes = [16, 24, 32, 48, 64, 128] as const

export function encodeJumpListIcon(source: NativeImage): Buffer {
  if (source.isEmpty()) throw new Error('A Jump List icon needs decoded artwork.')
  const { width, height } = source.getSize()
  const side = Math.min(width, height)
  const square = source.crop({
    x: Math.floor((width - side) / 2),
    y: Math.floor((height - side) / 2),
    width: side,
    height: side,
  })
  const frames = jumpListIconSizes.map((size) =>
    square.resize({ width: size, height: size, quality: 'best' }).toPNG(),
  )
  const header = Buffer.alloc(6 + frames.length * 16)
  header.writeUInt16LE(1, 2)
  header.writeUInt16LE(frames.length, 4)
  let offset = header.length
  frames.forEach((frame, index) => {
    const entry = 6 + index * 16
    header[entry] = jumpListIconSizes[index]!
    header[entry + 1] = jumpListIconSizes[index]!
    header.writeUInt16LE(1, entry + 4)
    header.writeUInt16LE(32, entry + 6)
    header.writeUInt32LE(frame.length, entry + 8)
    header.writeUInt32LE(offset, entry + 12)
    offset += frame.length
  })
  return Buffer.concat([header, ...frames])
}

export class JumpListIcons {
  constructor(
    private readonly directory: string,
    private readonly load: (key: CoverKey, signal?: AbortSignal) => Promise<string | null>,
    private readonly decode: (source: string) => NativeImage,
  ) {}

  async get(key: CoverKey | null | undefined, signal?: AbortSignal): Promise<string | null> {
    signal?.throwIfAborted()
    if (!key) return null
    try {
      const source = await this.load(key, signal)
      signal?.throwIfAborted()
      if (!source) return null
      const bytes = encodeJumpListIcon(this.decode(source))
      const root = join(this.directory, 'jump-list-icons')
      const path = join(root, `${createHash('sha256').update(bytes).digest('hex').toUpperCase()}.ico`)
      try {
        if ((await readFile(path)).equals(bytes)) return path
      } catch {}
      signal?.throwIfAborted()
      await mkdir(root, { recursive: true })
      const temporary = `${path}.${randomUUID()}.tmp`
      try {
        await writeFile(temporary, bytes, { flag: 'wx', signal })
        signal?.throwIfAborted()
        await rename(temporary, path)
      } finally {
        await rm(temporary, { force: true })
      }
      return path
    } catch (error) {
      signal?.throwIfAborted()
      return null
    }
  }
}

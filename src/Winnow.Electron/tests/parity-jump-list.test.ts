import { createHash } from 'node:crypto'
import { join } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { jumpListAppId, jumpListArguments, JumpListPublisher, type JumpListGame } from '../src/main/jump-list'
import { quoteArgument } from '../src/main/activation'
import { profileDirectory } from '../src/main/storage'

const game: JumpListGame = { ownershipId: 42, title: 'Jump List smoke game', workId: 1, coverWorkId: 1 }
const tick = () => new Promise<void>((resolve) => setImmediate(resolve))

describe('source Windows Jump List identity and argument contracts', () => {
  it('isolated directories have distinct stable taskbar identities and preserve the existing Electron profile identity', () => {
    const local = join(process.cwd(), 'local-app-data')
    const userData = join(local, 'Winnow Afterglow')
    const directory = join(local, 'winnow-jump-list-identity')
    const priorId = (root: string) =>
      `Winnow.Electron.${createHash('sha256').update(profileDirectory(userData, root).toLowerCase()).digest('hex').slice(0, 16)}`
    expect(jumpListAppId(directory, local, userData)).toBe(jumpListAppId(`${directory}/`, local, userData))
    expect(jumpListAppId(directory, local, userData)).not.toBe(
      jumpListAppId(`${directory}-other`, local, userData),
    )
    expect(jumpListAppId(directory, local, userData)).toBe(priorId(directory))
    expect(jumpListAppId(join(local, 'Winnow'), local, userData)).toBe(priorId(join(local, 'Winnow')))
    expect(jumpListAppId(join(local, 'Hoard'), local, userData)).toBe(
      jumpListAppId(join(local, 'Winnow'), local, userData),
    )
  })

  it.each([
    ['a b', '"a b"'],
    ['C:\\data\\', '"C:\\data\\\\"'],
    ['a"b', '"a\\"b"'],
  ])('preserves source Windows argument %j as %j', (input, expected) => {
    expect(quoteArgument(input)).toBe(expected)
  })

  it('every game and fullscreen destination carries its isolated directory in development and packaged arguments', () => {
    const directory = 'C:\\Temp\\Winnow jump list test'
    const packaged = jumpListArguments(directory, '--jump-list-game 42')
    expect(packaged).toContain('--jump-list-game 42 --data-dir ')
    expect(packaged).toBe('--jump-list-game 42 --data-dir "C:\\Temp\\Winnow jump list test"')
    expect(jumpListArguments(directory, '--jump-list-fullscreen')).toBe(
      '--jump-list-fullscreen --data-dir "C:\\Temp\\Winnow jump list test"',
    )
    expect(jumpListArguments(directory, '--jump-list-game 42', 'C:\\dev folder\\main.js')).toBe(
      '"C:\\dev folder\\main.js" --jump-list-game 42 --data-dir "C:\\Temp\\Winnow jump list test"',
    )
    expect(jumpListArguments(undefined, '--jump-list-game 42')).toBe('--jump-list-game 42')
    expect(jumpListArguments(undefined, '--jump-list-fullscreen')).toBe('--jump-list-fullscreen')
  })
})

describe('Jump List artwork publication lifetime', () => {
  it('a shell privacy failure cannot reject the background publication task', async () => {
    const controller = new JumpListPublisher({
      publish: () => {
        throw Error('Custom destinations are disabled')
      },
      loadIcon: async () => null,
    })
    await expect(controller.refresh([game])).resolves.toBeUndefined()
    controller.dispose()
  })

  it('publishes generic tasks before a held image and uses the known existing icon on the next immediate snapshot', async () => {
    let release!: (value: string | null) => void
    const publish = vi.fn()
    const loadIcon = vi.fn(
      () =>
        new Promise<string | null>((resolve) => {
          release = resolve
        }),
    )
    const controller = new JumpListPublisher({ publish, loadIcon, exists: () => true })
    const pending = controller.refresh([game])
    expect(publish).toHaveBeenCalledExactlyOnceWith([{ ...game, iconPath: undefined }])
    await tick()
    release('content-addressed.ico')
    await pending
    expect(publish).toHaveBeenLastCalledWith([{ ...game, iconPath: 'content-addressed.ico' }])
    const next = controller.refresh([game])
    expect(publish).toHaveBeenLastCalledWith([{ ...game, iconPath: 'content-addressed.ico' }])
    await tick()
    release(null)
    await next
    expect(publish).toHaveBeenLastCalledWith([{ ...game, iconPath: undefined }])
    controller.dispose()
  })

  it('rejects late publication from an old generation even when its artwork ignores cancellation', async () => {
    let release!: (value: string | null) => void
    const publish = vi.fn()
    const controller = new JumpListPublisher({
      publish,
      loadIcon: () =>
        new Promise((resolve) => {
          release = resolve
        }),
    })
    const pending = controller.refresh([game])
    await tick()
    await controller.refresh([])
    await pending
    release('obsolete.ico')
    await tick()
    expect(publish).toHaveBeenCalledTimes(2)
    expect(publish).toHaveBeenLastCalledWith([])
    controller.dispose()
  })

  it('disposal cancels the active request and cannot republish a late completion', async () => {
    let release!: (value: string | null) => void
    let signal!: AbortSignal
    const publish = vi.fn()
    const controller = new JumpListPublisher({
      publish,
      loadIcon: (_game, value) => {
        signal = value
        return new Promise((resolve) => {
          release = resolve
        })
      },
    })
    const pending = controller.refresh([game])
    await tick()
    controller.dispose()
    expect(signal.aborted).toBe(true)
    await pending
    release('late.ico')
    await tick()
    await controller.refresh([game])
    expect(publish).toHaveBeenCalledTimes(1)
  })

  it('a bounded missing or failing artwork read leaves usable fallback destinations', async () => {
    const publish = vi.fn()
    const controller = new JumpListPublisher({ publish, timeoutMs: 1, loadIcon: () => new Promise(() => {}) })
    await controller.refresh([game])
    expect(publish).toHaveBeenLastCalledWith([{ ...game, iconPath: undefined }])
    controller.dispose()
    const failed = new JumpListPublisher({
      publish,
      loadIcon: async () => {
        throw Error('missing art')
      },
    })
    await failed.refresh([game])
    expect(publish).toHaveBeenLastCalledWith([{ ...game, iconPath: undefined }])
    failed.dispose()
  })
})

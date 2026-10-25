import { mkdir, mkdtemp, rmdir, unlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { deriveExecutableFacts, inspectExecutable, readExecutableVersion } from '../src/main/executable-facts'

describe('ManualGameFromExecutableTests source contracts', () => {
  it('The_file_description_names_the_game', () => {
    expect(
      deriveExecutableFacts('D:\\Games\\HK\\hk.exe', 'Hollow Knight', 'Hollow Knight', 'Team Cherry'),
    ).toEqual({
      executablePath: 'D:\\Games\\HK\\hk.exe',
      installPath: 'D:\\Games\\HK',
      title: 'Hollow Knight',
      titleSource: 'file-description',
      publisher: 'Team Cherry',
    })
  })
  it('The_product_name_answers_when_there_is_no_description', () => {
    expect(deriveExecutableFacts('D:\\Games\\CH\\ch.exe', null, 'Cuphead', 'StudioMDHR')).toMatchObject({
      title: 'Cuphead',
      titleSource: 'product-name',
    })
  })
  it('A_stub_description_falls_through_to_the_folder', () => {
    expect(
      deriveExecutableFacts('D:\\Games\\Hollow Knight\\launcher.exe', 'Launcher', 'Launcher'),
    ).toMatchObject({ title: 'Hollow Knight', titleSource: 'folder-name' })
  })
  it('A_shipping_binary_walks_up_past_its_build_folders', () => {
    expect(
      deriveExecutableFacts('D:\\Games\\Deep Rock Galactic\\Binaries\\Win64\\FSD-Win64-Shipping.exe'),
    ).toMatchObject({
      title: 'Deep Rock Galactic',
      titleSource: 'folder-name',
      installPath: 'D:\\Games\\Deep Rock Galactic\\Binaries\\Win64',
    })
  })
  it.each(['Unreal Engine 4', 'UnrealEngine', 'UE4', 'Unity', 'GameMaker'])(
    'An_engine_name_is_not_a_game_title: %s',
    (description) => {
      expect(
        deriveExecutableFacts('D:\\Games\\Outer Wilds\\OuterWilds.exe', description, description),
      ).toMatchObject({ title: 'Outer Wilds', titleSource: 'folder-name' })
    },
  )
  it('Underscores_and_build_suffixes_are_cleaned_off', () => {
    expect(deriveExecutableFacts('C:\\g\\hollow_knight_x64.exe')).toMatchObject({
      title: 'hollow knight',
      titleSource: 'file-name',
    })
  })
  it('An_executable_that_says_nothing_proposes_no_title', () => {
    expect(deriveExecutableFacts('C:\\Games\\game.exe')).toMatchObject({
      title: null,
      titleSource: 'none',
      installPath: 'C:\\Games',
    })
  })
  it('An_unset_company_is_not_a_publisher', () => {
    expect(deriveExecutableFacts('D:\\Games\\X\\x.exe', 'Fez', null, 'DefaultCompany')).toMatchObject({
      title: 'Fez',
      publisher: null,
    })
  })
  it('A_file_that_is_not_a_program_still_yields_the_path', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'winnow-executable-'))
    const folder = join(directory, 'Chasm')
    await mkdir(folder)
    const path = join(folder, 'notes.txt')
    try {
      await writeFile(path, 'This is not an executable. Never run this fixture.')
      const read = await readExecutableVersion(path)
      expect(read.fileDescription ?? null).toBeNull()
      expect(await inspectExecutable(path)).toMatchObject({
        executablePath: path,
        installPath: folder,
        title: 'Chasm',
      })
    } finally {
      await unlink(path)
      await rmdir(folder)
      await rmdir(directory)
    }
  })
  it('A_missing_file_does_not_throw', async () => {
    const path = join(tmpdir(), 'winnow-missing-file-parity', 'Braid', 'b.exe')
    expect(await inspectExecutable(path)).toMatchObject({ title: 'Braid', titleSource: 'folder-name' })
  })
  it('keeps the chosen path and falls back when the resource reader fails', async () => {
    const read = vi.fn().mockRejectedValue(Error('Access denied'))
    expect(await inspectExecutable('D:\\Games\\Tunic\\Tunic.exe', read)).toMatchObject({
      title: 'Tunic',
      installPath: 'D:\\Games\\Tunic',
    })
    expect(read).toHaveBeenCalledWith('D:\\Games\\Tunic\\Tunic.exe')
  })
  it.skipIf(process.platform !== 'win32')(
    'reads real version resources without starting the chosen executable',
    async () => {
      const file = resolve('node_modules/electron/dist/electron.exe')
      const version = await readExecutableVersion(file)
      expect(version.productName).toBe('Electron')
      expect(version.fileDescription).toBe('Electron')
    },
  )
  it('supports POSIX paths and stops before using filesystem roots as titles', () => {
    expect(deriveExecutableFacts('/games/Outer Wilds/bin/game')).toMatchObject({
      title: 'Outer Wilds',
      installPath: '/games/Outer Wilds/bin',
    })
    expect(deriveExecutableFacts('C:\\game.exe').title).toBeNull()
    expect(deriveExecutableFacts('/game').title).toBeNull()
    expect(() => deriveExecutableFacts(' ')).toThrow('Choose an executable path')
  })
})

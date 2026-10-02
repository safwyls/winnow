import { resolve } from 'node:path'

const executable = process.platform === 'win32' ? '.exe' : ''
// Native tests require a built companion; they never fall back to dotnet run.
export const prebuiltBackend = resolve(
  process.env.WINNOW_BACKEND_PATH ?? `../Winnow.Backend/bin/Debug/net10.0/Winnow.Backend${executable}`,
)
export const prebuiltActivationHelper = resolve(process.env.WINNOW_ACTIVATION_HELPER_PATH ?? prebuiltBackend)
export const prebuiltFixture = resolve(
  process.env.WINNOW_ELECTRON_FIXTURE_PATH ??
    `../../tests/Winnow.Electron.Fixtures/bin/Debug/net10.0/Winnow.Electron.Fixtures${executable}`,
)

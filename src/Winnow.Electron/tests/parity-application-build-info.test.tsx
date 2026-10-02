// @vitest-environment jsdom
import { cleanup, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { applicationBuildInfo } from '../src/main/application-build-info'
import { buildInformationalVersion } from '../build/application-metadata'
import { ApplicationPreferences } from '../src/renderer/features/SettingsPreferences'
import type { ApiRequest, ApplicationInfo, WinnowBridge } from '../src/shared/bridge'
import '../src/preload/index'

const preload = vi.hoisted(() => ({ bridge: null as unknown as WinnowBridge, invoke: vi.fn() }))
vi.mock('electron', () => ({
  ipcRenderer: { invoke: preload.invoke, on: vi.fn(), removeListener: vi.fn() },
  contextBridge: {
    exposeInMainWorld: (_name: string, value: WinnowBridge) => {
      preload.bridge = value
    },
  },
}))

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

it.each(['desktop', 'fullscreen'] as const)(
  'Settings_reads_the_application_assembly_identity (%s)',
  async (mode) => {
    vi.stubGlobal('__WINNOW_BUILD_INFORMATIONAL_VERSION__', buildInformationalVersion(process.cwd()))
    const current = applicationBuildInfo(false, '44.4.5')
    const info: ApplicationInfo = {
      ...current,
      platform: 'win32',
      packaged: false,
      autostartSupported: false,
      openAtLogin: false,
    }
    preload.invoke.mockImplementation(async (channel: string, input?: ApiRequest) => {
      if (channel === 'winnow:application:info') return info
      if (channel === 'winnow:request')
        return {
          ok: true,
          status: 200,
          data: input?.route === 'plugins.directory' ? { directory: 'C:\\fixture\\plugins' } : [],
        }
      return undefined
    })
    Object.defineProperty(window, 'winnow', { configurable: true, value: preload.bridge })
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <ApplicationPreferences mode={mode} />
      </QueryClientProvider>,
    )
    const about = await screen.findByRole('region', { name: 'About Winnow' })
    const facts = within(about)
      .getAllByRole('definition')
      .map((value) => value.textContent)
    expect(
      within(about)
        .getAllByRole('term')
        .map((value) => value.textContent),
    ).toEqual(['Version', 'Source commit'])
    expect(facts).toEqual([current.version, current.commit])
    expect(current.version).not.toBe('Unknown')
    expect(current.version).not.toBe('44.4.5')
    expect(preload.invoke).toHaveBeenCalledWith('winnow:application:info')
    client.clear()
  },
)

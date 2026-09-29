// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { WinnowBridge } from '../src/shared/bridge'
import { DEFAULT_PROFILE, type ThemeDefinition, type ThemeProfile } from '../src/shared/theme'
import { DEFAULT_ARTWORK_EFFECTS } from '../src/shared/artworkEffects'
import { ThemeStudio } from '../src/renderer/theming/ThemeStudio'
import { useThemeRuntime } from '../src/renderer/theming/runtime'

const builtins: ThemeDefinition[] = [{ apiVersion: 1, id: 'avalon', name: 'Avalon' }]
let profile: ThemeProfile
let systemReduced: boolean
const mediaListeners = new Set<() => void>()

function Studio({ mode }: { mode: 'desktop' | 'fullscreen' }) {
  const runtime = useThemeRuntime(builtins)
  return <div className={mode}>{!runtime.loading && <ThemeStudio runtime={runtime} />}</div>
}

function followControl() {
  return screen.getByRole<HTMLInputElement>('checkbox', { name: /Follow the pointer/ })
}
function tiltControl() {
  return screen.getByRole<HTMLInputElement>('slider', { name: /Maximum tilt/ })
}
function setSystemReduced(value: boolean) {
  act(() => {
    systemReduced = value
    mediaListeners.forEach((listener) => listener())
  })
}

beforeEach(() => {
  profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.artwork = { ...DEFAULT_ARTWORK_EFFECTS, finish: 'foil', tilt: 9 }
  systemReduced = false
  mediaListeners.clear()
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({
      get matches() {
        return systemReduced
      },
      addEventListener: (_: string, listener: () => void) => mediaListeners.add(listener),
      removeEventListener: (_: string, listener: () => void) => mediaListeners.delete(listener),
    })),
  )
  window.winnow = {
    loadPreferences: vi.fn(async () => profile),
    savePreferences: vi.fn(async () => {}),
    listThemes: vi.fn(async () => []),
  } as unknown as WinnowBridge
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe.each(['desktop', 'fullscreen'] as const)('Theme Studio motion controls in %s', (mode) => {
  it.each([false, true])(
    'preserves artwork after an accent change with reduced motion %s',
    async (reduced) => {
      profile.appearance.reducedMotion = reduced
      render(<Studio mode={mode} />)
      const accent = await screen.findByRole('textbox', { name: 'Accent color' })
      fireEvent.change(accent, { target: { value: '#efad80' } })
      const expected = structuredClone(profile)
      expected.appearance.accent = '#efad80'
      await waitFor(() => expect(window.winnow.savePreferences).toHaveBeenLastCalledWith(expected))
      expect(followControl().checked).toBe(true)
      expect(followControl().disabled).toBe(reduced)
      expect(tiltControl().disabled).toBe(reduced)
      expect(tiltControl().value).toBe('9')
      expect(screen.getByRole<HTMLInputElement>('checkbox', { name: /Floating artwork/ }).disabled).toBe(
        false,
      )

      if (reduced) {
        expect(screen.getByRole('status').textContent).toContain('Reduce motion is on.')
        fireEvent.click(screen.getByRole('button', { name: 'Turn off reduced motion' }))
        expected.appearance.reducedMotion = false
        await waitFor(() => expect(window.winnow.savePreferences).toHaveBeenLastCalledWith(expected))
        expect(followControl().disabled).toBe(false)
        expect(tiltControl().disabled).toBe(false)
        expect(screen.queryByRole('status')).toBeNull()
        expect(screen.getByRole<HTMLInputElement>('checkbox', { name: /^Reduce motion/ }).checked).toBe(false)
      }
    },
  )

  it('respects live system changes without offering to bypass them or changing saved choices', async () => {
    render(<Studio mode={mode} />)
    await screen.findByRole('textbox', { name: 'Accent color' })
    await waitFor(() => expect(window.winnow.savePreferences).toHaveBeenCalled())
    vi.mocked(window.winnow.savePreferences).mockClear()

    setSystemReduced(true)
    expect(screen.getByRole('status').textContent).toContain('Your system requests reduced motion.')
    expect(screen.queryByRole('button', { name: 'Turn off reduced motion' })).toBeNull()
    expect(followControl().disabled).toBe(true)
    expect(tiltControl().disabled).toBe(true)
    setSystemReduced(false)
    expect(followControl().disabled).toBe(false)
    expect(tiltControl().disabled).toBe(false)
    expect(followControl().checked).toBe(true)
    expect(tiltControl().value).toBe('9')
    expect(window.winnow.savePreferences).not.toHaveBeenCalled()

    fireEvent.click(followControl())
    expect(tiltControl().disabled).toBe(true)
    fireEvent.click(followControl())
    expect(tiltControl().disabled).toBe(false)
    expect(tiltControl().value).toBe('9')
  })
})

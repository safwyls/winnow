// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { LibraryHideConfirmation } from '../src/renderer/features/LibraryHideConfirmation'
import { ExplicitVisibility } from '../src/renderer/features/ExplicitVisibility'

afterEach(cleanup)
function mount(content: React.ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(<QueryClientProvider client={client}>{content}</QueryClientProvider>)
}
function bridge(request: ReturnType<typeof vi.fn>) {
  Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  it(`${mode} bulk hide starts on Cancel and dismissal writes nothing`, async () => {
    const request = vi.fn(),
      close = vi.fn(),
      hidden = vi.fn()
    bridge(request)
    mount(<LibraryHideConfirmation mode={mode} workIds={[1, 3]} close={close} hidden={hidden} />)
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Cancel' })))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(close).toHaveBeenCalledOnce()
    expect(hidden).not.toHaveBeenCalled()
    expect(request).not.toHaveBeenCalled()
  })
  it(`${mode} failed bulk hide retains the captured group for a single successful retry`, async () => {
    const request = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, status: 500, message: 'Storage unavailable' })
      .mockResolvedValueOnce({ ok: true, status: 204 })
    const hidden = vi.fn(),
      close = vi.fn()
    bridge(request)
    mount(<LibraryHideConfirmation mode={mode} workIds={[1, 3]} close={close} hidden={hidden} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Hide 2 games' }))
    await screen.findByRole('alert')
    expect(screen.getByRole('alert').textContent).toContain('Storage unavailable')
    expect(hidden).not.toHaveBeenCalled()
    expect(close).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Hide 2 games' }))
    await waitFor(() => expect(hidden).toHaveBeenCalledOnce())
    expect(request).toHaveBeenCalledTimes(2)
    for (const [input] of request.mock.calls)
      expect(input).toEqual({
        route: 'hidden.put',
        params: undefined,
        body: { workIds: [1, 3], hidden: true },
      })
  })
  it(`${mode} repeated confirm cannot dispatch a second in-flight hide`, async () => {
    let finish!: (value: unknown) => void
    const request = vi.fn(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    const close = vi.fn(),
      hidden = vi.fn()
    bridge(request)
    mount(<LibraryHideConfirmation mode={mode} workIds={[1, 3]} close={close} hidden={hidden} />)
    const button = await screen.findByRole('button', { name: 'Hide 2 games' })
    act(() => {
      fireEvent.click(button)
      fireEvent.click(button)
    })
    await waitFor(() => expect(request).toHaveBeenCalledOnce())
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(close).not.toHaveBeenCalled()
    await act(async () => finish({ ok: true, status: 204 }))
    await waitFor(() => expect(hidden).toHaveBeenCalledOnce())
  })
}
it('explicit visibility reports the authoritative affected tile count and the zero-evidence note', async () => {
  const request = vi.fn().mockResolvedValue({ ok: true, status: 200, data: { explicitHidden: 2 } })
  bridge(request)
  const first = mount(<ExplicitVisibility />)
  await screen.findByText('titles hidden when explicit content is off.', { exact: false })
  expect(screen.getByLabelText('Explicit content visibility').textContent).toContain('2 titles hidden')
  first.unmount()
  request.mockResolvedValue({ ok: true, status: 200, data: { explicitHidden: 0 } })
  mount(<ExplicitVisibility />)
  await screen.findByText('No titles identified as explicit yet.')
})
it('an unavailable explicit count exposes retry instead of claiming no titles are affected', async () => {
  bridge(vi.fn().mockResolvedValue({ ok: false, status: 500, message: 'offline' }))
  mount(<ExplicitVisibility />)
  await screen.findByRole('button', { name: 'Retry count' })
  expect(screen.queryByText('No titles identified as explicit yet.')).toBeNull()
})

it('successful Hide waits for the active library snapshot before returning focus', async () => {
  let refresh!: (value: number[]) => void
  const read = vi
    .fn()
    .mockResolvedValueOnce([1, 3])
    .mockImplementation(
      () =>
        new Promise<number[]>((resolve) => {
          refresh = resolve
        }),
    )
  const hidden = vi.fn()
  bridge(vi.fn().mockResolvedValue({ ok: true, status: 204 }))
  function Projection() {
    const result = useQuery({ queryKey: ['api', 'library.get'], queryFn: read })
    return <span>{result.data?.join(',')}</span>
  }
  mount(
    <>
      <Projection />
      <LibraryHideConfirmation mode="desktop" workIds={[1, 3]} close={vi.fn()} hidden={hidden} />
    </>,
  )
  await screen.findByText('1,3')
  const confirm = screen.getByRole('button', { name: 'Hide 2 games' })
  fireEvent.click(confirm)
  await waitFor(() => expect(read).toHaveBeenCalledTimes(2))
  expect(hidden).not.toHaveBeenCalled()
  expect(confirm.hasAttribute('disabled')).toBe(true)
  await act(async () => refresh([]))
  await waitFor(() => expect(hidden).toHaveBeenCalledOnce())
  expect(read).toHaveBeenCalledTimes(2)
})

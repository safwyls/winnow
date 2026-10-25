// @vitest-environment jsdom
import { useEffect, useRef, useState } from 'react'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { AvalonAction, AvalonActions } from '../src/renderer/themes/avalon-actions'

afterEach(cleanup)

function Fixture({
  choose = () => {},
  unavailable = () => {},
  detached = () => {},
  originalInert = false,
}: {
  choose?(): void
  unavailable?(): void
  detached?(): void
  originalInert?: boolean
}) {
  const [open, setOpen] = useState(false)
  const [nested, setNested] = useState(false)
  const [editor, setEditor] = useState(false)
  const trigger = useRef<HTMLButtonElement>(null)
  return (
    <div className="avalon-shell fullscreen">
      <div data-testid="preexisting" inert={originalInert || undefined} />
      <section aria-label="Origin" data-testid="origin" hidden={editor}>
        <RetainedProbe detached={detached} />
        <button
          ref={trigger}
          onClick={() => {
            setNested(false)
            setOpen(true)
          }}
        >
          More
        </button>
        <button>Play</button>
      </section>
      {editor && (
        <section aria-label="Editor">
          <button
            onClick={() => {
              setEditor(false)
              requestAnimationFrame(() => trigger.current?.focus())
            }}
          >
            Back to origin
          </button>
        </section>
      )}
      <AvalonActions
        open={open}
        close={() => setOpen(false)}
        title={nested ? 'Remove this game?' : 'More actions'}
        restoreFocus={() => {
          if (!editor) trigger.current?.focus()
        }}
      >
        {nested ? (
          <AvalonAction label="Keep game" onChoose={choose} />
        ) : (
          <>
            <AvalonAction label="Unavailable action" disabled onChoose={unavailable} />
            <AvalonAction
              label="Pin this game"
              description="Keep it near the top of your library."
              onChoose={choose}
            />
            <AvalonAction label="Edit game details" onChoose={() => setEditor(true)} />
            <AvalonAction
              label="Remove game"
              onChoose={() => {
                setNested(true)
                setOpen(true)
              }}
            />
          </>
        )}
      </AvalonActions>
    </div>
  )
}
function RetainedProbe({ detached }: { detached(): void }) {
  useEffect(() => () => detached(), [detached])
  return <span>Retained origin</span>
}
async function open() {
  const trigger = screen.getByRole('button', { name: 'More' })
  trigger.focus()
  fireEvent.click(trigger)
  await screen.findByRole('dialog', { name: 'More actions' })
  await waitFor(() =>
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Pin this game' })),
  )
  return trigger
}

it.each(['keyboard', 'right-click'] as const)(
  'dismisses with %s and returns to the retained origin trigger',
  async (input) => {
    const detached = vi.fn()
    render(<Fixture detached={detached} />)
    const origin = screen.getByTestId('origin')
    const trigger = await open()
    expect(origin.isConnected).toBe(true)
    expect(origin.hidden).toBe(false)
    expect(origin.hasAttribute('inert')).toBe(true)
    const panel = screen.getByRole('dialog')
    if (input === 'keyboard') fireEvent.keyDown(panel, { key: 'Escape' })
    else fireEvent.contextMenu(within(panel).getByRole('button', { name: 'Pin this game' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(document.activeElement).toBe(trigger))
    expect(screen.getByTestId('origin')).toBe(origin)
    expect(origin.hasAttribute('inert')).toBe(false)
    expect(detached).not.toHaveBeenCalled()
  },
)

it('invokes an enabled choice once, closes the panel and refuses a disabled choice', async () => {
  const choose = vi.fn(),
    unavailable = vi.fn()
  render(<Fixture choose={choose} unavailable={unavailable} />)
  const trigger = await open()
  const disabled = screen.getByRole('button', { name: 'Unavailable action' }) as HTMLButtonElement
  disabled.focus()
  expect(document.activeElement).not.toBe(disabled)
  fireEvent.click(disabled)
  expect(unavailable).not.toHaveBeenCalled()
  expect(screen.getByRole('dialog')).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'Pin this game' }))
  expect(choose).toHaveBeenCalledTimes(1)
  await waitFor(() => expect(document.activeElement).toBe(trigger))
  expect(screen.queryByRole('dialog')).toBeNull()
})

it('keeps the origin attached through a nested menu and restores its original trigger', async () => {
  const detached = vi.fn(),
    choose = vi.fn()
  render(<Fixture detached={detached} choose={choose} />)
  const origin = screen.getByTestId('origin')
  const trigger = await open()
  fireEvent.click(screen.getByRole('button', { name: 'Remove game' }))
  const nested = await screen.findByRole('dialog', { name: 'Remove this game?' })
  expect(screen.getAllByRole('dialog')).toHaveLength(1)
  expect(origin.isConnected).toBe(true)
  expect(origin.hasAttribute('inert')).toBe(true)
  expect(document.activeElement).toBe(within(nested).getByRole('button', { name: 'Keep game' }))
  fireEvent.click(screen.getByRole('button', { name: 'Keep game' }))
  await waitFor(() => expect(document.activeElement).toBe(trigger))
  expect(choose).toHaveBeenCalledTimes(1)
  expect(detached).not.toHaveBeenCalled()
})

it('closes before opening a child editor and returns without reopening the old menu', async () => {
  render(<Fixture />)
  const trigger = await open()
  fireEvent.click(screen.getByRole('button', { name: 'Edit game details' }))
  expect(screen.queryByRole('dialog')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Back to origin' }))
  await waitFor(() => expect(document.activeElement).toBe(trigger))
  expect(screen.queryByRole('dialog')).toBeNull()
})

it('skips disabled choices during directional navigation and preserves a preexisting inert region', async () => {
  render(<Fixture originalInert />)
  await open()
  fireEvent.keyDown(document.activeElement!, { key: 'End' })
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Remove game' }))
  fireEvent.keyDown(document.activeElement!, { key: 'Home' })
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Pin this game' }))
  fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' })
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Edit game details' }))
  fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
  await act(async () => {
    await Promise.resolve()
  })
  expect(screen.getByTestId('preexisting').hasAttribute('inert')).toBe(true)
})

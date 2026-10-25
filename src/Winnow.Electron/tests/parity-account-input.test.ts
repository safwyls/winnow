import { describe, expect, it, vi } from 'vitest'
import { AccountInputGate } from '../src/main/account-input'
import { accountComposerKeys, accountInputDocument } from '../src/main/account-composer'

function fixture() {
  let resolve!: () => void
  const waiting = new Promise<void>((done) => {
    resolve = done
  })
  const ready = vi.fn(() => waiting),
    contents = {
      focus: vi.fn(),
      sendInputEvent: vi.fn(),
      insertText: vi.fn(async () => {}),
      isDestroyed: vi.fn(() => false),
    }
  return { gate: new AccountInputGate(contents, ready), ready, contents, resolve }
}
describe('account browser input gate', () => {
  it('drops both key and text input while locked without ever waiting for an unready browser', async () => {
    const { gate, ready, contents } = fixture()
    gate.setEnabled(false)
    await gate.key({ key: 'Enter', shift: false })
    await gate.text('local draft')
    expect(ready).not.toHaveBeenCalled()
    expect(contents.insertText).not.toHaveBeenCalled()
    expect(contents.sendInputEvent).not.toHaveBeenCalled()
  })
  it.each(['lock', 'navigate', 'dispose'])(
    'discards key and composed text when %s occurs during readiness',
    async (kind) => {
      const { gate, contents, resolve } = fixture(),
        key = gate.key({ key: 'Enter', shift: false }),
        text = gate.text('never insert')
      if (kind === 'lock') gate.setEnabled(false)
      if (kind === 'navigate') gate.navigated()
      if (kind === 'dispose') contents.isDestroyed.mockReturnValue(true)
      resolve()
      await Promise.all([key, text])
      expect(contents.insertText).not.toHaveBeenCalled()
      expect(contents.sendInputEvent).not.toHaveBeenCalled()
    },
  )
  it('rechecks a lock that was lifted while waiting and only accepts freshly requested input', async () => {
    const { gate, contents, resolve } = fixture(),
      old = gate.text('old')
    gate.setEnabled(false)
    gate.setEnabled(true)
    resolve()
    await old
    expect(contents.insertText).not.toHaveBeenCalled()
    await gate.text('new')
    expect(contents.insertText).toHaveBeenCalledExactlyOnceWith('new')
  })
  it('sends physical key sequences and inserts only newly composed text without executing provider JavaScript', async () => {
    const { gate, contents, resolve } = fixture()
    resolve()
    await gate.key({ key: 'Enter', shift: false })
    await gate.key({ key: 'Tab', shift: true })
    await gate.text('Typed é 🔒')
    expect(contents.sendInputEvent.mock.calls.slice(0, 3).map(([event]) => event)).toEqual([
      { type: 'keyDown', keyCode: 'Enter', modifiers: [] },
      { type: 'char', keyCode: '\r', modifiers: [] },
      { type: 'keyUp', keyCode: 'Enter', modifiers: [] },
    ])
    expect(contents.sendInputEvent).toHaveBeenCalledWith({
      type: 'keyDown',
      keyCode: 'Tab',
      modifiers: ['shift'],
    })
    expect(contents.insertText).toHaveBeenCalledWith('Typed é 🔒')
  })
  it('refuses empty or unbounded composed text', async () => {
    const { gate, ready } = fixture()
    await gate.text('')
    await gate.text('x'.repeat(4097))
    expect(ready).not.toHaveBeenCalled()
  })
  it('always starts masked and empty with a scriptless document and separate character, case and symbol controls', () => {
    const html = accountInputDocument(true)
    expect(html).toContain('type="password"')
    expect(html).not.toContain('value=')
    expect(html).toContain("script-src 'none'")
    expect(accountComposerKeys(false, false)).toContain('a')
    expect(accountComposerKeys(true, false)).toContain('A')
    expect(accountComposerKeys(false, true)).toContain('@')
    for (const key of ['Space', 'Backspace', 'Shift', 'Symbols', 'Done', 'Cancel'])
      expect(accountComposerKeys(false, false)).toContain(key)
  })
})

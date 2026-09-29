import { useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { mergeAnswer, mergeMemberLabels, mergeTitle, type MergeCard } from './parity-merge-model'
import { mergeIdle, mergePlaytime } from './parity-merge-facts'
import { storeLabel } from '../api/client'

function move(event: KeyboardEvent<HTMLDivElement>) {
  if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return
  const buttons = [...event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')]
  const index = buttons.indexOf(document.activeElement as HTMLButtonElement)
  if (index < 0) return
  event.preventDefault()
  event.stopPropagation()
  const next =
    event.key === 'Home'
      ? 0
      : event.key === 'End'
        ? buttons.length - 1
        : Math.max(0, Math.min(buttons.length - 1, index + (event.key === 'ArrowDown' ? 1 : -1)))
  buttons[next]?.focus()
  buttons[next]?.scrollIntoView?.({ block: 'nearest' })
}

export function MergeMemberSheet({
  card,
  disabled,
  onClose,
  onPromote,
  onInclude,
  onSelect,
  onLink,
  onDismiss,
  onSeparate,
  onHeader,
  onOpenGame,
  onReview,
}: {
  card: MergeCard
  disabled: boolean
  onClose(): void
  onPromote(workId: number): void
  onInclude(workId: number, value: boolean): void
  onSelect(): void
  onLink(): void
  onDismiss(): void
  onSeparate(): void
  onHeader(store: string): void
  onOpenGame?: (workId: number) => void
  onReview(): void
}) {
  const [member, setMember] = useState<number | null>(null)
  const [confirm, setConfirm] = useState(false)
  const [header, setHeader] = useState(false)
  const content = useRef<HTMLDivElement>(null)
  const origin = useRef(document.activeElement as HTMLElement | null)
  const row = card.rows.find((entry) => entry.workId === member)
  const labels = mergeMemberLabels(card)
  const answerable = !disabled && mergeAnswer(card).childWorkIds.length > 0
  useLayoutEffect(() => {
    content.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
  }, [member, confirm, header])
  function back() {
    if (confirm) setConfirm(false)
    else if (header) setHeader(false)
    else if (member !== null) setMember(null)
    else onClose()
  }
  function finish(action: () => void) {
    onClose()
    action()
  }
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) back()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content
          ref={content}
          className="dialog-content feature-panel merge-sheet mode-fullscreen"
          onKeyDown={move}
          onPointerDownOutside={(event) => event.preventDefault()}
          onEscapeKeyDown={(event) => {
            event.preventDefault()
            event.stopPropagation()
            back()
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            if (origin.current?.isConnected) origin.current.focus()
          }}
        >
          <Dialog.Title>
            {header
              ? `Header store for ${mergeTitle(card)}`
              : confirm
                ? 'Group these entries?'
                : row
                  ? row.title
                  : mergeTitle(card)}
          </Dialog.Title>
          <Dialog.Description>
            {confirm
              ? `Group these entries under ${mergeTitle(card)}? Nothing is deleted.`
              : row
                ? [row.stores.map(storeLabel).join(', '), mergePlaytime(row), mergeIdle(row)]
                    .filter(Boolean)
                    .join(' · ')
                : card.reason || 'Your entries remain available in this saved group.'}
          </Dialog.Description>
          <div className="merge-sheet-actions">
            {header && card.header ? (
              <>
                {card.header.options.map((option) => (
                  <button
                    key={option.value}
                    disabled={disabled}
                    aria-pressed={card.header!.store === option.value}
                    onClick={() => {
                      onHeader(option.value)
                      setHeader(false)
                    }}
                  >
                    {option.label}
                  </button>
                ))}
                <button onClick={() => setHeader(false)}>Back to proposal</button>
              </>
            ) : confirm ? (
              <>
                <button disabled={!answerable} onClick={() => finish(onLink)}>
                  Continue
                </button>
                <button onClick={() => setConfirm(false)}>Cancel</button>
              </>
            ) : row ? (
              <>
                {onOpenGame && (
                  <button disabled={disabled} onClick={() => finish(() => onOpenGame(row.workId))}>
                    Open game
                  </button>
                )}
                {!card.actId && card.kind === 'same_game' && row.workId !== card.parent && (
                  <button
                    disabled={disabled}
                    onClick={() => {
                      onPromote(row.workId)
                      setMember(null)
                    }}
                  >
                    Make header
                  </button>
                )}
                {!card.actId && row.workId !== card.parent && (
                  <button
                    disabled={disabled}
                    onClick={() => {
                      onInclude(row.workId, !card.included.includes(row.workId))
                      setMember(null)
                    }}
                  >
                    {card.included.includes(row.workId) ? 'Leave out' : 'Include'}
                  </button>
                )}
                <button onClick={() => setMember(null)}>Back to proposal</button>
              </>
            ) : (
              <>
                {card.header && (
                  <button disabled={disabled} onClick={() => setHeader(true)}>
                    Header store ·{' '}
                    {card.header.options.find((option) => option.value === card.header!.store)?.label}
                  </button>
                )}
                {card.rows.map((entry, index) => (
                  <button key={entry.workId} onClick={() => setMember(entry.workId)}>
                    {labels[index]} ·{' '}
                    {entry.workId === card.parent
                      ? 'Header'
                      : card.included.includes(entry.workId)
                        ? 'Included'
                        : 'Left out'}
                  </button>
                ))}
                {card.actId ? (
                  <button disabled={disabled} onClick={() => finish(onSeparate)}>
                    Separate again
                  </button>
                ) : (
                  <>
                    <button data-controller-context disabled={!answerable} onClick={onSelect}>
                      {card.selected ? 'Remove from selection' : 'Select for grouping'}
                    </button>
                    <button data-controller-play disabled={!answerable} onClick={() => setConfirm(true)}>
                      Same game
                    </button>
                    <button disabled={disabled} onClick={() => finish(onDismiss)}>
                      Different games
                    </button>
                    <button disabled={disabled} onClick={() => finish(onReview)}>
                      {card.kind === 'same_game' ? 'Same game…' : 'Review relationship…'}
                    </button>
                  </>
                )}
                <button onClick={onClose}>Back to proposals</button>
              </>
            )}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

export function MergeBatchConfirmation({
  count,
  onClose,
  onConfirm,
}: {
  count: number
  onClose(): void
  onConfirm(): void
}) {
  const origin = useRef(document.activeElement as HTMLElement | null)
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content
          className="dialog-content feature-panel merge-sheet mode-fullscreen"
          onKeyDown={move}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            if (origin.current?.isConnected) origin.current.focus()
          }}
        >
          <Dialog.Title>
            Group {count} {count === 1 ? 'proposal' : 'proposals'}?
          </Dialog.Title>
          <Dialog.Description>Use the chosen header for each group. Nothing is deleted.</Dialog.Description>
          <div className="merge-sheet-actions">
            <button
              onClick={() => {
                onClose()
                onConfirm()
              }}
            >
              Continue
            </button>
            <button onClick={onClose}>Cancel</button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

export function MergeOptionsSheet({
  disabled = false,
  title,
  value,
  options,
  onChoose,
  onClose,
}: {
  disabled?: boolean
  title: string
  value: string
  options: { value: string; label: string }[]
  onChoose(value: string): void
  onClose(): void
}) {
  const origin = useRef(document.activeElement as HTMLElement | null)
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content
          className="dialog-content feature-panel merge-sheet mode-fullscreen"
          onKeyDown={move}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            if (origin.current?.isConnected) origin.current.focus()
          }}
        >
          <Dialog.Title>{title}</Dialog.Title>
          <Dialog.Description>Choose an option for possible matches.</Dialog.Description>
          <div className="merge-sheet-actions">
            {options.map((option) => (
              <button
                key={option.value}
                disabled={disabled}
                aria-pressed={value === option.value}
                onClick={() => {
                  onClose()
                  onChoose(option.value)
                }}
              >
                {option.label}
              </button>
            ))}
            <button onClick={onClose}>Cancel</button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

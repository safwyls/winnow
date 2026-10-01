import { useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { mergeAnswer, mergeMemberLabels, mergeTitle, type MergeCard } from './parity-merge-model'
import { mergeIdle, mergePlaytime } from './parity-merge-facts'
import { storeLabel } from '../api/client'
import { mergeActionCopy, mergeActionNames } from './parity-merge-copy'
import { Notice } from './shared'
import { restoreFocusWhenReady } from './restore-focus'
import acceptGlyph from './assets/xbox_button_a_outline.svg?raw'
import backGlyph from './assets/xbox_button_b_outline.svg?raw'

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
  headerProblem,
  headerBusy,
  onRecheck,
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
  onHeader(store: string): Promise<boolean>
  headerProblem?: unknown
  headerBusy?: boolean
  onRecheck?(): Promise<boolean>
  onOpenGame?: (workId: number) => void
  onReview(): void
}) {
  const [member, setMember] = useState<number | null>(null)
  const [confirm, setConfirm] = useState(false)
  const [header, setHeader] = useState(false)
  const content = useRef<HTMLDivElement>(null)
  const headerOrigin = useRef<HTMLButtonElement>(null)
  const recovery = useRef<HTMLButtonElement>(null)
  const headerVersion = useRef(0)
  const restoreHeader = useRef(false)
  const origin = useRef(document.activeElement as HTMLElement | null)
  const row = card.rows.find((entry) => entry.workId === member)
  const labels = mergeMemberLabels(card)
  const answerable = !disabled && mergeAnswer(card).childWorkIds.length > 0
  useLayoutEffect(() => {
    if (restoreHeader.current && !header) {
      restoreHeader.current = false
      restoreFocusWhenReady(headerOrigin.current)
      return
    }
    content.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
  }, [member, confirm, header])
  function back() {
    headerVersion.current++
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
                    onClick={async () => {
                      const version = ++headerVersion.current
                      const saved = await onHeader(option.value)
                      if (version !== headerVersion.current || !content.current) return
                      if (saved) {
                        restoreHeader.current = true
                        setHeader(false)
                      } else restoreFocusWhenReady(recovery.current)
                    }}
                  >
                    {option.label}
                  </button>
                ))}
                {onRecheck && (
                  <button
                    ref={recovery}
                    disabled={headerBusy}
                    onClick={async () => {
                      if (await onRecheck())
                        restoreFocusWhenReady(
                          content.current?.querySelector('button[aria-pressed="true"]') ?? null,
                        )
                    }}
                  >
                    Check saved review
                  </button>
                )}
                <Notice
                  error={headerProblem}
                  message={headerBusy ? 'Saving your header choice…' : undefined}
                />
                <button onClick={back}>Back to proposal</button>
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
                  <button ref={headerOrigin} disabled={disabled} onClick={() => setHeader(true)}>
                    Header store ·{' '}
                    {card.header.options.find((option) => option.value === card.header!.store)?.label}
                  </button>
                )}
                {card.rows.map((entry, index) => (
                  <button
                    className="merge-sheet-member"
                    data-merge-member={entry.workId}
                    aria-label={`${labels[index]} · ${entry.workId === card.parent ? 'Header' : card.included.includes(entry.workId) ? 'Included' : 'Left out'}`}
                    aria-description={`Owned on ${entry.stores.map(storeLabel).join(', ')}`}
                    key={entry.workId}
                    onClick={() => setMember(entry.workId)}
                  >
                    <span>
                      {labels[index]} ·{' '}
                      {entry.workId === card.parent
                        ? 'Header'
                        : card.included.includes(entry.workId)
                          ? 'Included'
                          : 'Left out'}
                    </span>
                    <span className="merge-row-stores">
                      {entry.stores.map((store) => (
                        <span className="merge-store" key={store}>
                          {store === 'epic' ? 'EPIC' : storeLabel(store).toLocaleUpperCase()}
                        </span>
                      ))}
                    </span>
                  </button>
                ))}
                {card.actId ? (
                  <button
                    disabled={disabled}
                    aria-label={mergeActionNames(card).separate}
                    title={mergeActionCopy.separateTip}
                    onClick={() => finish(onSeparate)}
                  >
                    {mergeActionCopy.separate}
                  </button>
                ) : (
                  <>
                    <button data-controller-context disabled={!answerable} onClick={onSelect}>
                      {card.selected ? 'Remove from selection' : 'Select for grouping'}
                    </button>
                    <button
                      data-controller-play
                      disabled={!answerable}
                      aria-label={mergeActionNames(card).same}
                      title={mergeActionCopy.sameTip}
                      onClick={() => setConfirm(true)}
                    >
                      {mergeActionCopy.same}
                    </button>
                    <button
                      disabled={disabled}
                      aria-label={mergeActionNames(card).different}
                      title={mergeActionCopy.differentTip}
                      onClick={() => finish(onDismiss)}
                    >
                      {mergeActionCopy.different}
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
          {header && (
            <div className="merge-header-hints">
              {[
                [acceptGlyph, 'A', 'Choose'],
                [backGlyph, 'B', 'Back'],
              ].map(([art, key, label]) => (
                <span key={key}>
                  <span
                    aria-hidden="true"
                    data-merge-header-glyph={key}
                    dangerouslySetInnerHTML={{ __html: art.replace('<svg ', '<svg viewBox="8 8 48 48" ') }}
                  />
                  <span className="sr-only">{key} </span>
                  {label}
                </span>
              ))}
            </div>
          )}
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
  options: readonly { value: string; label: string }[]
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

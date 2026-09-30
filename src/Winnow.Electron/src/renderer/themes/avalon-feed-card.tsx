import { useLayoutEffect, useRef, type KeyboardEvent } from 'react'
import type { ThemeContext } from '../../shared/theme'
import { AvalonCover } from './avalon'
import { AddToListButton } from '../features/parity-list-prompt'
import { feedDate } from './avalon-feed'
import { receiptDuration, type FeedDeck, type FeedDeckShelf, type FeedRow } from './avalon-feed-model'
import './avalon-feed-card.css'

function ActionIcon({ kind }: { kind: 'list' | 'later' | 'never' }) {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 16 16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      aria-hidden="true"
      data-feed-icon={kind}
    >
      <path
        d={
          kind === 'list'
            ? 'M2 3H12M2 8H8M2 13H8M12 9V15M9 12H15'
            : kind === 'later'
              ? 'M8 1A7 7 0 1 1 7.99 1ZM8 4V8L11 10'
              : 'M8 1A7 7 0 1 1 7.99 1ZM3 13L13 3'
        }
      />
    </svg>
  )
}

/** Desktop feedback shares the cover's measured frame; fullscreen keeps its hero actions. */
export function AvalonFeedCard({
  context,
  deck,
  shelf,
  row,
  onKeyDown,
}: {
  context: ThemeContext
  deck: FeedDeck
  shelf: FeedDeckShelf
  row: FeedRow
  onKeyDown?: (event: KeyboardEvent<HTMLButtonElement>) => void
}) {
  const host = useRef<HTMLDivElement>(null),
    restore = useRef(false)
  useLayoutEffect(() => {
    if (restore.current) {
      host.current
        ?.querySelector<HTMLButtonElement>(row.receipt ? '.avalon-feed-undo' : '.avalon-cover')
        ?.focus({ preventScroll: true })
      restore.current = false
    }
  }, [row.receipt])
  const give = (kind: number, undo = false) => {
    restore.current = !!host.current?.contains(document.activeElement)
    void deck.respond(row, kind, undo)
  }
  const receipt = row.receipt,
    countdown = receipt && deck.canReplace(shelf)
  return (
    <div className="avalon-feed-card-host" ref={host}>
      <AvalonCover
        context={context}
        game={row.game}
        reason={row.reason}
        onKeyDown={onKeyDown}
        feed={{
          actions: (
            <>
              <AddToListButton
                games={[row.game]}
                mode="desktop"
                origin="feed"
                iconOnly
                label="Add to list"
                icon={<ActionIcon kind="list" />}
              />
              {shelf.feedback && row.releaseId !== undefined && (
                <>
                  <button aria-label="Not now" title="Not now" disabled={row.pending} onClick={() => give(1)}>
                    <ActionIcon kind="later" />
                  </button>
                  <button
                    aria-label="Not interested"
                    title="Not interested"
                    disabled={row.pending}
                    onClick={() => give(0)}
                  >
                    <ActionIcon kind="never" />
                  </button>
                </>
              )}
            </>
          ),
          receipt: receipt && (
            <div className="avalon-feed-card-receipt">
              <span role="status">
                {receipt.kind === 1 ? (
                  <>
                    Back on{' '}
                    <time className="avalon-feed-date" dateTime={receipt.expiresAt ?? undefined}>
                      {feedDate(receipt.expiresAt)}
                    </time>
                  </>
                ) : (
                  'Off the feed.'
                )}
                {countdown && <span className="sr-only"> Undo before this card is replaced.</span>}
              </span>
              <button
                className="avalon-feed-undo"
                disabled={row.pending}
                onClick={() => give(receipt.kind, true)}
              >
                Undo
              </button>
            </div>
          ),
          countdown: countdown && (
            <span
              className="avalon-feed-card-countdown"
              title="Replaced by the next suggestion. Undo is on the history screen."
            >
              <progress
                className="sr-only"
                aria-label="Time before this card is replaced"
                max={receiptDuration}
                value={receipt.elapsed}
              />
              <svg width="18" height="18" viewBox="0 0 18 18" aria-hidden="true">
                <circle cx="9" cy="9" r="7" fill="none" stroke="var(--line)" strokeWidth="2" />
                <circle
                  cx="9"
                  cy="9"
                  r="7"
                  fill="none"
                  stroke="var(--cool-foreground, var(--cool))"
                  strokeWidth="2"
                  pathLength="1"
                  strokeDasharray="1"
                  strokeDashoffset={Math.min(1, receipt.elapsed / receiptDuration)}
                  transform="rotate(-90 9 9)"
                />
              </svg>
            </span>
          ),
        }}
      />
      {row.error && (
        <p className="avalon-feed-card-error" role="alert">
          {row.error}
        </p>
      )}
    </div>
  )
}

import { useApiQuery } from '../api/hooks'

/** Count the shared query's disappearing tiles, including grouped editions. */
export function ExplicitVisibility() {
  const counts = useApiQuery<{ explicitHidden: number }>('library.visibility')
  return (
    <div className="muted" aria-label="Explicit content visibility" aria-live="polite">
      {counts.error ? (
        <p className="reading-prose">
          The explicit content count could not be loaded.{' '}
          <button onClick={() => void counts.refetch()}>Retry count</button>
        </p>
      ) : counts.data?.explicitHidden === undefined ? (
        <p className="reading-prose">Reading explicit content count…</p>
      ) : counts.data.explicitHidden === 0 ? (
        <p className="reading-prose">No titles identified as explicit yet.</p>
      ) : (
        <p className="reading-prose">
          <span className="avalon-feed-date">{counts.data.explicitHidden.toLocaleString()}</span>{' '}
          {counts.data.explicitHidden === 1 ? 'title hidden' : 'titles hidden'} when explicit content is off.
        </p>
      )}
    </div>
  )
}

import { useId } from 'react'
import type { Mode } from '../api/types'
import { metadataSyncExplanation, useMetadataSync } from './metadata-sync'

export function MetadataSyncSettings({ mode = 'desktop' }: { mode?: Mode }) {
  const state = useMetadataSync()
  const id = useId()
  return (
    <section
      className={
        mode === 'fullscreen' ? 'fullscreen-settings-content metadata-sync' : 'feature-panel metadata-sync'
      }
      aria-label="Library metadata"
    >
      <h2 className={mode === 'fullscreen' ? 'fullscreen-settings-group' : undefined}>Library metadata</h2>
      <p>{metadataSyncExplanation}</p>
      {/* Fullscreen keeps the busy row focused so Down still reaches the next source action. */}
      <button
        type="button"
        className={mode === 'fullscreen' ? 'fullscreen-setting-row fullscreen-setting-action' : undefined}
        aria-label="Sync metadata now"
        aria-describedby={id}
        aria-disabled={state.busy}
        disabled={mode === 'desktop' && state.busy}
        onClick={() => {
          void state.operation.sync()
        }}
      >
        <span>Sync metadata now</span>
        {mode === 'fullscreen' && (
          <span className="fullscreen-setting-cue" aria-hidden="true">
            Run Ⓐ
          </span>
        )}
      </button>
      <p id={id} role="status" aria-live="polite" aria-atomic="true">
        {state.status}
      </p>
    </section>
  )
}

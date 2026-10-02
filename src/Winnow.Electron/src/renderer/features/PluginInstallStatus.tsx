import { usePluginInstallation } from './plugin-installation'
import './plugin-installation.css'

export function PluginInstallStatus({ back }: { back?: () => void }) {
  const state = usePluginInstallation()
  if (!state.request) return null
  return (
    <section
      className={`plugin-installation ${back ? 'plugin-installation-page' : 'feature-panel'}`}
      aria-label="Plugin installation"
    >
      <h2>Plugin installation</h2>
      <p role="status" aria-live="polite">
        {state.status}
      </p>
      {state.busy && <progress aria-label="Installing plugin" />}
      <div className="form-actions">
        {state.canRetry && (
          <button aria-label="Retry plugin installation" onClick={() => void state.installation.retry()}>
            Retry installation
          </button>
        )}
        {state.busy && <button onClick={() => void state.installation.cancel()}>Cancel installation</button>}
        {back && (
          <button autoFocus data-controller-back onClick={back}>
            Back
          </button>
        )}
      </div>
    </section>
  )
}

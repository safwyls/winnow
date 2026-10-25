import { IdentityTools } from './parity-library'
import type { Mode } from '../api/types'

export function Merges({ mode, onOpenGame }: { mode: Mode; onOpenGame(workId: number): void }) {
  return (
    <section className={`feature-page mode-${mode}`} aria-label="Merges">
      <header className="feature-heading">
        <h1>Merges</h1>
      </header>
      <IdentityTools mode={mode} onOpenGame={onOpenGame} />
    </section>
  )
}

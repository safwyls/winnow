import '../../src/renderer/styles.css'
import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MergeQueue } from '../../src/renderer/features/parity-merge'
import { useIdentityReview } from '../../src/renderer/features/parity-merge-query'
import { AVALON_PALETTES, avalonPaletteStyle } from '../../src/renderer/themes/avalon-palettes'
import { mergeFixture } from '../parity-merge-fixtures'
import type { ApiRequest } from '../../src/shared/bridge'

let review = mergeFixture()
const original = structuredClone(review)
const probe = {
  opened: [] as number[],
  writes: [] as ApiRequest[],
  pauseTransitions: false,
  transitions: [] as { property: string; duration: number; target: string }[],
  configure: (_options: { reduced?: boolean; mode?: 'desktop' | 'fullscreen' }) => {},
}
document.addEventListener(
  'transitionrun',
  (event) => {
    const target = event.target as HTMLElement
    if (!target.matches('.merge-row,.merge-reason')) return
    for (const animation of target.getAnimations()) {
      if (!(animation instanceof CSSTransition)) continue
      probe.transitions.push({
        property: animation.transitionProperty,
        duration: Number(animation.effect!.getTiming().duration),
        target: target.className,
      })
      if (probe.pauseTransitions) {
        animation.pause()
        animation.currentTime = Number(animation.effect!.getTiming().duration) / 2
      }
    }
  },
  true,
)
Object.assign(window, {
  mergeSurfaceProbe: probe,
  winnow: {
    cancelRequest: async () => {},
    request: async (input: ApiRequest) => {
      if (input.route === 'identity.get') return { ok: true, status: 200, data: structuredClone(review) }
      if (input.route === 'artworkState')
        return { ok: true, status: 200, data: { current: null, revision: 'art' } }
      if (input.route === 'preferences.presentation.get') return { ok: true, status: 200, data: [] }
      probe.writes.push(input)
      const body = input.body as any
      review.revision += '1'
      if (input.route === 'identity.link') {
        for (const child of body.childWorkIds)
          review.history.push({
            id: child + 100,
            actId: 100,
            parentWorkId: body.parentWorkId,
            childWorkId: child,
            kind: body.kind,
          })
      } else if (input.route === 'identity.dismiss') {
        review.candidates = review.candidates.filter((candidate) => !body.candidateIds.includes(candidate.id))
      } else if (input.route === 'identity.undo') {
        review = { ...structuredClone(original), revision: review.revision }
      }
      return { ok: true, status: 200, data: { revision: review.revision, actId: 100 } }
    },
  },
})
const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
function Probe() {
  const [options, setOptions] = useState({ reduced: false, mode: 'desktop' as 'desktop' | 'fullscreen' })
  probe.configure = (patch) => setOptions((state) => ({ ...state, ...patch }))
  const query = useIdentityReview()
  return (
    <div
      className={`mode-${options.mode}${options.reduced ? ' reduced-motion' : ''}`}
      style={{
        ...avalonPaletteStyle(AVALON_PALETTES[0]!),
        padding: 30,
        minHeight: '100vh',
        background: 'var(--background)',
        color: 'var(--text)',
      }}
    >
      <button id="outside">Outside the queue</button>
      {query.data && (
        <MergeQueue
          review={query.data}
          mode={options.mode}
          onReview={() => {}}
          onOpenGame={(id) => probe.opened.push(id)}
        />
      )}
    </div>
  )
}
createRoot(document.getElementById('root')!).render(
  <QueryClientProvider client={client}>
    <Probe />
  </QueryClientProvider>,
)

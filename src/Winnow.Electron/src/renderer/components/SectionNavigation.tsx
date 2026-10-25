import type { ReactNode } from 'react'
import leftTrigger from '../features/assets/xbox_lt_outline.svg?raw'
import rightTrigger from '../features/assets/xbox_rt_outline.svg?raw'
import leftBumper from '../features/assets/xbox_lb_outline.svg?raw'
import rightBumper from '../features/assets/xbox_rb_outline.svg?raw'
import './section-navigation.css'

export function SectionNavigation({ fullscreen, children }: { fullscreen: boolean; children: ReactNode }) {
  if (!fullscreen) return children
  return (
    <div className="fullscreen-section-navigation">
      <Trigger direction="previous" artwork={leftTrigger} />
      {children}
      <Trigger direction="next" artwork={rightTrigger} />
    </div>
  )
}

export function RootBumper({ button }: { button: 'LB' | 'RB' }) {
  const artwork = button === 'LB' ? leftBumper : rightBumper
  return (
    <span
      className="section-trigger"
      data-root-bumper={button}
      aria-hidden="true"
      dangerouslySetInnerHTML={{ __html: artwork.replace('<svg ', '<svg viewBox="0 0 64 64" ') }}
    />
  )
}

function Trigger({ direction, artwork }: { direction: string; artwork: string }) {
  return (
    <span
      className="section-trigger"
      data-section-trigger={direction}
      aria-hidden="true"
      dangerouslySetInnerHTML={{ __html: artwork.replace('<svg ', '<svg viewBox="0 0 64 64" ') }}
    />
  )
}

// Reserve the bold label's width before selection so every neighbor keeps its position.
export function SectionLabel({ children }: { children: string }) {
  return (
    <span className="section-label" data-label={children}>
      <span>{children}</span>
    </span>
  )
}

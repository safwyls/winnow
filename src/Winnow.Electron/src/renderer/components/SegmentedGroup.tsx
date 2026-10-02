import type { ComponentProps } from 'react'
import './segmented-group.css'

export function SegmentedGroup({
  label,
  children,
  className = 'segmented',
  ...props
}: Omit<ComponentProps<'div'>, 'role' | 'aria-label'> & { label: string }) {
  return (
    <div {...props} className={className} role="group" aria-label={label}>
      <div className="segmented-clip">{children}</div>
    </div>
  )
}

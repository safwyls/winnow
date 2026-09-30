import { useLayoutEffect, useRef } from 'react'
import { ChevronDown, ChevronUp } from 'lucide-react'
import './avalon-shelf-indicator.css'

/** Mouse targets share the current shelf with controller input without adding focus stops. */
export function AvalonShelfIndicator({
  titles,
  selected,
  onSelect,
}: {
  titles: string[]
  selected: number
  onSelect(index: number): void
}) {
  const root = useRef<HTMLDivElement>(null)
  const current = Math.max(0, Math.min(titles.length - 1, selected))
  const height = (titles.length + 2) * 44 + (titles.length + 1) * 4
  useLayoutEffect(() => {
    const element = root.current!
    const resize = () =>
      element.style.setProperty('--shelf-indicator-scale', String(Math.min(1, element.clientHeight / height)))
    resize()
    const observer = new ResizeObserver(resize)
    observer.observe(element)
    return () => observer.disconnect()
  }, [height])
  return (
    <div ref={root} className="avalon-shelf-navigation" role="group" aria-label="Recommendation shelves">
      <div className="avalon-shelf-indicator">
        <button
          tabIndex={-1}
          aria-label="Previous shelf"
          disabled={current === 0 || titles.length === 0}
          onPointerDown={(event) => event.preventDefault()}
          onClick={() => onSelect(current - 1)}
        >
          <ChevronUp aria-hidden="true" />
        </button>
        {titles.map((title, index) => (
          <button
            tabIndex={-1}
            key={`${index}:${title}`}
            data-controller-tab
            aria-label={`Show ${title}`}
            aria-description={`${title}, shelf ${index + 1} of ${titles.length}`}
            aria-current={index === current ? 'true' : undefined}
            title={`${title}, shelf ${index + 1} of ${titles.length}`}
            onPointerDown={(event) => event.preventDefault()}
            onClick={() => onSelect(index)}
          >
            <i aria-hidden="true" />
          </button>
        ))}
        <button
          tabIndex={-1}
          aria-label="Next shelf"
          disabled={titles.length === 0 || current === titles.length - 1}
          onPointerDown={(event) => event.preventDefault()}
          onClick={() => onSelect(current + 1)}
        >
          <ChevronDown aria-hidden="true" />
        </button>
      </div>
    </div>
  )
}

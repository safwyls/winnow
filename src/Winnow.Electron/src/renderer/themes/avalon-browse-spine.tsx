import { useEffect, useMemo, useRef, useState, type PointerEvent, type RefObject } from 'react'
import {
  browseSections,
  isAlphabetSort,
  nearestSection,
  spineHalo,
  spineLocation,
  spinePointerRow,
  spineWave,
} from './avalon-browse-policy'
import './avalon-browse-spine.css'

export function AvalonBrowseSpine({
  games,
  sort,
  scroll,
  jump,
}: {
  games: readonly { title: string }[]
  sort: string
  scroll: RefObject<HTMLDivElement | null>
  jump(index: number): void
}) {
  const sections = useMemo(() => browseSections(games, sort), [games, sort])
  const alphabetical = isAlphabetSort(sort)
  const [pointer, setPointer] = useState<number | null>(null)
  const [proportion, setProportion] = useState(0)
  const dragging = useRef<number | null>(null)
  const previous = useRef<number | undefined>(undefined)
  useEffect(() => {
    const element = scroll.current
    if (!element) return
    const update = () => {
      const extent = element.scrollHeight - element.clientHeight
      setProportion(extent > 0 ? element.scrollTop / extent : 0)
    }
    update()
    element.addEventListener('scroll', update)
    const observer = new ResizeObserver(update)
    observer.observe(element)
    return () => {
      element.removeEventListener('scroll', update)
      observer.disconnect()
    }
  }, [scroll, games, sort])

  const location = pointer ?? spineLocation(games, sort, proportion)
  function move(event: PointerEvent<HTMLDivElement>, scrub: boolean) {
    const bounds = event.currentTarget.getBoundingClientRect()
    const y = event.clientY - bounds.top
    const row = spinePointerRow(y, bounds.height)
    setPointer(row)
    if (!scrub || bounds.height <= 0) return
    if (alphabetical) {
      const index = nearestSection(sections, row)
      if (index !== undefined && index !== previous.current) {
        previous.current = index
        jump(index)
      }
    } else if (scroll.current) {
      const element = scroll.current
      element.scrollTop =
        Math.max(0, element.scrollHeight - element.clientHeight) * Math.max(0, Math.min(1, y / bounds.height))
    }
  }

  if (!games.length) return null
  return (
    <div
      className="avalon-browse-spine"
      role="group"
      aria-label={alphabetical ? 'Browse by letter' : 'Browse this order'}
      title={alphabetical ? 'Drag to browse by letter' : 'Drag to browse this order'}
      onPointerDown={(event) => {
        if (event.button !== 0) return
        event.preventDefault()
        dragging.current = event.pointerId
        previous.current = undefined
        event.currentTarget.setPointerCapture(event.pointerId)
        move(event, true)
      }}
      onPointerMove={(event) => move(event, dragging.current === event.pointerId)}
      onPointerUp={(event) => {
        if (dragging.current !== event.pointerId) return
        move(event, true)
        dragging.current = null
        if (event.currentTarget.hasPointerCapture(event.pointerId))
          event.currentTarget.releasePointerCapture(event.pointerId)
        const bounds = event.currentTarget.getBoundingClientRect()
        if (
          event.clientX < bounds.left ||
          event.clientX > bounds.right ||
          event.clientY < bounds.top ||
          event.clientY > bounds.bottom
        )
          setPointer(null)
      }}
      onLostPointerCapture={() => {
        dragging.current = null
        previous.current = undefined
      }}
      onPointerCancel={() => {
        dragging.current = null
        setPointer(null)
      }}
      onPointerLeave={() => {
        if (dragging.current === null) setPointer(null)
      }}
    >
      {sections.map((section, stop) => {
        const style = { transform: `translateX(${spineWave(stop, pointer)}px)` }
        const halo = spineHalo(stop, location)
        return alphabetical ? (
          <button
            key={section.label}
            type="button"
            className="avalon-alpha-stop"
            aria-label={`Jump to ${section.label}`}
            disabled={section.index === undefined}
            data-halo={halo}
            style={style}
            onClick={(event) => {
              // Pointer scrubbing is owned by the rail, including unavailable stops.
              if (event.detail === 0 && section.index !== undefined) jump(section.index)
            }}
          >
            <span>{section.label}</span>
          </button>
        ) : (
          <div
            key={section.label}
            className="avalon-sort-notch"
            aria-hidden="true"
            data-halo={halo}
            style={style}
          >
            <span />
          </div>
        )
      })}
    </div>
  )
}

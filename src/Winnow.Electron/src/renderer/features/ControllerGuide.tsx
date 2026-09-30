import controllerArt from './assets/controller.svg?raw'
import './controller-guide.css'

const left = [
  ['LB / RB', 'Main screens'],
  ['LT / RT', 'Tabs & shelves'],
  ['✚ / LS', 'Move'],
  ['▱', 'Search'],
  ['☰', 'Quick menu'],
]
const right = [
  ['Y', 'More / filters / reset'],
  ['B', 'Back / cancel'],
  ['A', 'Select'],
  ['X', 'Play / edit note'],
  ['RS', 'Scroll long content'],
]
export function ControllerGuide() {
  return (
    <section className="fullscreen-controller-guide" aria-label="Controller guide">
      <div className="controller-guide-diagram">
        <Mappings items={left} />
        <div className="controller-guide-art" aria-hidden="true">
          <div dangerouslySetInnerHTML={{ __html: controllerArt }} />
          <svg viewBox="0 0 64 52" className="controller-guide-face-buttons">
            {(
              [
                ['Y', 43, 21],
                ['X', 39, 25],
                ['B', 47, 25],
                ['A', 43, 29],
              ] as const
            ).map(([label, x, y]) => (
              <g key={label}>
                <circle cx={x} cy={y} r={1.8} />
                <text x={x} y={y}>
                  {label}
                </text>
              </g>
            ))}
          </svg>
        </div>
        <Mappings items={right} />
      </div>
      <p>
        Keyboard: arrows move · Enter selects · Escape returns. Your place is kept if a controller
        disconnects.
      </p>
    </section>
  )
}
function Mappings({ items }: { items: string[][] }) {
  return (
    <dl className="controller-guide-mappings">
      {items.map(([glyph, label]) => (
        <div key={label}>
          <dt>
            <span className="controller-guide-glyph" aria-hidden="true">
              {glyph}
            </span>
            <span className="sr-only">{glyph}</span>
          </dt>
          <dd>{label}</dd>
        </div>
      ))}
    </dl>
  )
}

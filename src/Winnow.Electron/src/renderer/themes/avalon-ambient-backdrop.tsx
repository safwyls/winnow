import activity from './avalon/assets/fullscreen-activity.svg?raw'
import settings from './avalon/assets/fullscreen-settings.svg?raw'
import './avalon-ambient-backdrop.css'

const artwork = {
  journal: activity.replace('<svg ', '<svg preserveAspectRatio="xMidYMid slice" '),
  settings: settings.replace('<svg ', '<svg preserveAspectRatio="xMidYMid slice" '),
}

/** Original decorative paths keep their theme roles and cover the whole presentation canvas. */
export function AvalonAmbientBackdrop({ page }: { page: keyof typeof artwork }) {
  return (
    <div
      className="avalon-ambient-backdrop"
      data-ambient-page={page}
      aria-hidden="true"
      dangerouslySetInnerHTML={{ __html: artwork[page] }}
    />
  )
}

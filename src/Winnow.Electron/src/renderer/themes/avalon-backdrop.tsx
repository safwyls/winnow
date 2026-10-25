import { useBackdrop, type BackdropProps } from '../components/useBackdrop'
import { BackdropImage } from '../components/BackdropImage'
import { ArtworkPlaceholder } from '../components/ArtworkPlaceholder'
import { backdropGeometry, backdropKey, type BackdropLayer } from '../components/backdrop-model'
import './avalon-backdrop.css'

export function AvalonBackdrop({
  fullscreen = true,
  cinematic = false,
  className = '',
  ...props
}: BackdropProps & { cinematic?: boolean }) {
  const { element, frame, size } = useBackdrop({ ...props, fullscreen })

  function layer(value: BackdropLayer | null, outgoing: boolean) {
    if (!value) return null
    const geometry = backdropGeometry(
      value.candidate,
      { ...size, fullscreen },
      value.pixels.width / value.pixels.height,
    )
    return (
      <div
        className="avalon-backdrop-layer"
        data-outgoing={outgoing || undefined}
        data-key={backdropKey(value.candidate.key)}
        data-fallback={value.fallback || undefined}
        style={{ opacity: outgoing ? 1 : frame.progress }}
      >
        <div
          className="avalon-backdrop-art"
          data-fitted={geometry.fitted || undefined}
          style={{ width: geometry.width, height: geometry.height, left: geometry.left }}
        >
          <BackdropImage source={value.pixels.source} />
          <div className="avalon-backdrop-veil" />
        </div>
      </div>
    )
  }
  return (
    <div
      ref={element}
      className={`avalon-backdrop ${className}`}
      aria-hidden="true"
      data-fullscreen={fullscreen || undefined}
      data-cinematic={cinematic || undefined}
      data-loading={frame.loading || undefined}
      data-crossfading={Boolean(frame.outgoing) || undefined}
      data-state={frame.current ? 'ready' : frame.loading ? 'loading' : 'missing'}
    >
      {!frame.current && !frame.loading && <ArtworkPlaceholder label="" />}
      {layer(frame.outgoing, true)}
      {layer(frame.current, false)}
      <div className="avalon-backdrop-reading-veil" />
    </div>
  )
}

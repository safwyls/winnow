import { ArtworkPlaceholder } from './ArtworkPlaceholder'
import { BackdropImage } from './BackdropImage'
import { backdropKey } from './backdrop-model'
import { useBackdrop, type BackdropProps } from './useBackdrop'

/** Generic hero presentation keeps each composition's crop, scrim and portal styles. */
export function BackdropArtwork({ className = '', ...props }: BackdropProps) {
  const { element, frame } = useBackdrop({ ...props, fitWholeHero: false })
  return (
    <div
      ref={element}
      className={`artwork backdrop-artwork ${className}`}
      aria-hidden="true"
      data-loading={frame.loading || undefined}
      data-state={frame.current ? 'ready' : frame.loading ? 'loading' : 'missing'}
    >
      {[frame.outgoing, frame.current].map(
        (layer, index) =>
          layer && (
            <div
              key={index}
              className="backdrop-artwork-layer"
              data-key={backdropKey(layer.candidate.key)}
              data-fallback={layer.fallback || undefined}
              style={{ opacity: index === 0 ? 1 : frame.progress }}
            >
              <BackdropImage source={layer.pixels.source} className="art-ready" />
            </div>
          ),
      )}
      {!frame.current &&
        (frame.loading ? (
          <div className="art-loading art-loading-hero">
            <span className="art-loading-orbit" />
          </div>
        ) : (
          <ArtworkPlaceholder />
        ))}
    </div>
  )
}

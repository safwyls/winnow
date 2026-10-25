import { MetadataSyncSettings } from './MetadataSyncSettings'
import { ArtworkSourcePreferences, artworkSourceExplanation } from './SettingsPreferences'
import { IgdbConnectionPanel } from './IgdbSettings'
import { FullscreenSettingsAction, useFullscreenSettingsEntry } from './FullscreenSettingRows'
import './metadata-settings.css'

export function FullscreenMetadataSettings({
  onOpen,
}: {
  onOpen(page: 'IGDB metadata' | 'Artwork source order'): void
}) {
  const entry = useFullscreenSettingsEntry(true)
  return (
    <section ref={entry} className="fullscreen-settings-content">
      <MetadataSyncSettings mode="fullscreen" />
      <h2 className="fullscreen-settings-group">Sources</h2>
      <FullscreenSettingsAction label="IGDB metadata" onClick={() => onOpen('IGDB metadata')} />
      <FullscreenSettingsAction label="Artwork source order" onClick={() => onOpen('Artwork source order')} />
      <p>{artworkSourceExplanation}</p>
    </section>
  )
}

export function FullscreenMetadataChild({
  title,
  onBack,
}: {
  title: 'IGDB metadata' | 'Artwork source order'
  onBack(): void
}) {
  return (
    <div
      className="fullscreen-settings-child-reading"
      data-settings-child-reading
      role="region"
      aria-label={title}
    >
      <div className="fullscreen-information-column">
        <h1>{title}</h1>
        {title === 'IGDB metadata' ? (
          <IgdbConnectionPanel mode="fullscreen" sectioned />
        ) : (
          <ArtworkSourcePreferences mode="fullscreen" />
        )}
        <button
          type="button"
          data-settings-child-back
          aria-label="Back to Metadata & artwork"
          onClick={onBack}
        >
          Back
        </button>
      </div>
    </div>
  )
}

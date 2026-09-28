import {
  ArrowDown,
  ArrowUp,
  Check,
  Download,
  LayoutTemplate,
  Palette,
  RotateCcw,
  Sparkles,
  Upload,
} from 'lucide-react'
import { useEffect, useId, useState } from 'react'
import {
  PALETTES,
  contrastRatio,
  resolvedThemeColors,
  themeSettingValues,
  type ThemeColorKey,
  type ThemeProfile,
  type ThemeSettingValue,
} from '../../shared/theme'
import type { ThemeRuntime } from './runtime'
import { normalizeArtworkEffects, type ArtworkEffectOptions } from '../../shared/artworkEffects'
import { useSystemReducedMotion } from '../useSystemReducedMotion'
import { useLibrary } from '../api/hooks'
import { Artwork } from '../components/Artwork'
import { PortalSurface } from '../components/portal-effects'
import './studio.css'

const sectionLabels: Record<string, string> = {
  hero: 'Featured game',
  returning: 'Return to a favorite',
  shelves: 'Discovery shelves',
}
const colorLabels: { key: ThemeColorKey; label: string }[] = [
  { key: 'background', label: 'Background' },
  { key: 'surface', label: 'Panels' },
  { key: 'raised', label: 'Raised surfaces' },
  { key: 'text', label: 'Text' },
  { key: 'muted', label: 'Secondary text' },
  { key: 'line', label: 'Borders' },
  { key: 'cool', label: 'Secondary accent' },
]

function RiftStudioPreview({
  settings,
  reducedMotion,
}: {
  settings: Record<string, ThemeSettingValue>
  reducedMotion: boolean
}) {
  const sample = useLibrary().data?.games[0]
  return (
    <PortalSurface
      className="studio-portal-sample"
      options={{
        roundness: Number(settings.portalRoundness),
        waviness: Number(settings.portalWaviness),
        activity: Number(settings.portalActivity),
      }}
      reducedMotion={reducedMotion}
      artwork={sample && <Artwork workId={sample.workId} hero eager />}
    >
      <div className="studio-portal-reading">
        <span className="studio-kicker">A window into your library</span>
        <h2>{sample?.title ?? 'A world within.'}</h2>
        <p>Adjust the portal shape and activity to see the edge respond here.</p>
      </div>
    </PortalSurface>
  )
}

function ColorControl({
  label,
  value,
  onChange,
  onReset,
}: {
  label: string
  value: string
  onChange(value: string): void
  onReset(): void
}) {
  const id = useId()
  const [draft, setDraft] = useState(value)
  useEffect(() => setDraft(value), [value])
  return (
    <div className="studio-field">
      <label htmlFor={id}>{label}</label>
      <div className="studio-color">
        <input
          type="color"
          aria-label={`${label} picker`}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
        <input
          id={id}
          type="text"
          value={draft}
          maxLength={7}
          spellCheck={false}
          aria-description="Six-digit hex color, such as #18191b"
          onChange={(event) => {
            const next = event.target.value
            setDraft(next)
            if (/^#[\da-f]{6}$/i.test(next)) onChange(next)
          }}
          onBlur={() => {
            if (!/^#[\da-f]{6}$/i.test(draft)) setDraft(value)
          }}
        />
        <button type="button" aria-label={`Reset ${label.toLowerCase()}`} onClick={onReset}>
          Reset
        </button>
      </div>
    </div>
  )
}

export function ThemeStudio({ runtime }: { runtime: ThemeRuntime }) {
  const { profile, setProfile, theme } = runtime
  const { appearance, layout } = profile
  const palette = PALETTES[appearance.palette]
  const colors = resolvedThemeColors(profile)
  const artwork = normalizeArtworkEffects(appearance.artwork)
  const systemReducedMotion = useSystemReducedMotion()
  const reducedMotion = appearance.reducedMotion || systemReducedMotion
  const motionStatusId = useId()
  const artworkChange = (patch: Partial<ArtworkEffectOptions>) =>
    setProfile((current) => ({
      ...current,
      appearance: {
        ...current.appearance,
        artwork: normalizeArtworkEffects(current.appearance.artwork, patch),
      },
    }))
  const appearanceChange = (patch: Partial<ThemeProfile['appearance']>) =>
    setProfile((current) => {
      const next = { ...current.appearance, ...patch }
      if (next.colors === undefined) delete next.colors
      if (next.scale === undefined) delete next.scale
      return { ...current, appearance: next }
    })
  const resetColor = (key: ThemeColorKey) => {
    const next = { ...appearance.colors }
    delete next[key]
    appearanceChange({ colors: Object.keys(next).length ? next : undefined })
  }
  const layoutChange = (patch: Partial<ThemeProfile['layout']>) =>
    setProfile((current) => ({ ...current, layout: { ...current.layout, ...patch } }))
  const themeSettings = themeSettingValues(theme, profile)
  const settingChange = (id: string, value: ThemeSettingValue) =>
    setProfile((current) => ({
      ...current,
      settings: { ...current.settings, [theme.id]: { ...current.settings[theme.id], [id]: value } },
    }))
  const moveSection = (index: number, offset: number) => {
    const next = [...layout.discoverSections]
    ;[next[index], next[index + offset]] = [next[index + offset], next[index]]
    layoutChange({ discoverSections: next })
  }
  const contrastIssues = [
    { label: 'Text on panels', ratio: contrastRatio(colors.text, colors.surface) },
    { label: 'Text on the background', ratio: contrastRatio(colors.text, colors.background) },
    { label: 'Secondary text on panels', ratio: contrastRatio(colors.muted, colors.surface) },
    { label: 'Accent on panels', ratio: contrastRatio(colors.accent, colors.surface) },
    { label: 'Secondary accent on panels', ratio: contrastRatio(colors.cool, colors.surface) },
  ].filter((check) => check.ratio < 4.5)
  const themes = [
    ...runtime.builtins.map((item) => ({
      ...item,
      version: 'Included',
      description:
        item.id === 'afterglow'
          ? 'Cinematic artwork, open space, and warm editorial type.'
          : item.id === 'rift'
            ? 'Floating covers, artwork portals, and a dense gallery beneath the stars.'
            : 'A compact library catalog with an index and a reading desk.',
    })),
    ...runtime.packages.filter((item) => !runtime.builtins.some((builtin) => builtin.id === item.id)),
  ]
  return (
    <div className="theme-studio">
      <header className="studio-heading">
        <div>
          <p className="studio-kicker">Make room for your taste</p>
          <h1>Your Winnow</h1>
          <p>Choose a composition. Make the details yours.</p>
        </div>
        <div className="studio-actions">
          <button type="button" onClick={() => void runtime.importProfile()}>
            <Upload size={16} /> Import profile
          </button>
          <button type="button" onClick={() => void runtime.exportProfile()}>
            <Download size={16} /> Export profile
          </button>
          <button type="button" onClick={runtime.resetProfile}>
            <RotateCcw size={16} /> Reset
          </button>
        </div>
      </header>
      {runtime.notice && (
        <div className="studio-notice" role="status">
          <span>{runtime.notice}</span>
          <button type="button" onClick={runtime.clearNotice} aria-label="Dismiss appearance message">
            Dismiss
          </button>
        </div>
      )}
      <div className="studio-body">
        <div className="studio-controls">
          <section className="studio-panel" aria-labelledby="studio-composition">
            <div className="studio-section-title">
              <LayoutTemplate size={18} />
              <h2 id="studio-composition">Composition</h2>
            </div>
            <p>Choose an independent design. Each remembers its colors and layout when you switch.</p>
            <div className="studio-theme-options">
              {themes.map((item) => (
                <button
                  type="button"
                  key={item.id}
                  className={`studio-theme-option${profile.themeId === item.id ? ' selected' : ''}`}
                  aria-pressed={profile.themeId === item.id}
                  onClick={() => runtime.selectTheme(item.id)}
                >
                  <span
                    className={`studio-layout-preview ${item.id === 'afterglow' ? 'preview-afterglow' : item.id === 'rift' ? 'preview-rift' : 'preview-catalogue'}`}
                    aria-hidden="true"
                  >
                    <i />
                    <i />
                    <i />
                    <i />
                  </span>
                  <span className="studio-theme-label">
                    <strong>{item.name}</strong>
                    {profile.themeId === item.id && <Check size={16} />}
                  </span>
                  <small>{item.description ?? `Installed theme · ${item.version}`}</small>
                </button>
              ))}
            </div>
            <div className="studio-install">
              <button type="button" onClick={() => void runtime.installTheme()}>
                <Upload size={16} /> Install a theme
              </button>
              <small>Choose an unpacked theme folder from an author you trust.</small>
            </div>
            {runtime.loading && <p role="status">Loading your theme…</p>}
          </section>
          <section className="studio-panel" aria-labelledby="studio-appearance">
            <div className="studio-section-title">
              <Palette size={18} />
              <h2 id="studio-appearance">Atmosphere</h2>
            </div>
            <fieldset className="studio-fieldset">
              <legend>Palette</legend>
              <div className="studio-palettes">
                {Object.entries(PALETTES).map(([id, colors]) => (
                  <button
                    type="button"
                    key={id}
                    aria-pressed={appearance.palette === id}
                    onClick={() =>
                      appearanceChange({
                        palette: id as ThemeProfile['appearance']['palette'],
                        accent: colors.accent,
                        colors: undefined,
                      })
                    }
                  >
                    <span className="studio-swatches" aria-hidden="true">
                      {[colors.background, colors.surface, colors.text, colors.accent].map((color, index) => (
                        <i key={index} style={{ background: color }} />
                      ))}
                    </span>
                    <span>{colors.name}</span>
                    {appearance.palette === id && <Check size={14} />}
                  </button>
                ))}
              </div>
            </fieldset>
            <div className="studio-field-grid">
              <ColorControl
                label="Accent color"
                value={appearance.accent}
                onChange={(accent) => appearanceChange({ accent })}
                onReset={() => appearanceChange({ accent: palette.accent })}
              />
              <label className="studio-field">
                Typography
                <select
                  value={appearance.font}
                  onChange={(event) =>
                    appearanceChange({ font: event.target.value as ThemeProfile['appearance']['font'] })
                  }
                >
                  <option value="editorial">Editorial · serif headings</option>
                  <option value="modern">Modern · sans serif</option>
                  <option value="mono">Monospace · typewriter</option>
                </select>
              </label>
              <label className="studio-field">
                Spacing
                <select
                  value={appearance.density}
                  onChange={(event) =>
                    appearanceChange({ density: event.target.value as ThemeProfile['appearance']['density'] })
                  }
                >
                  <option value="compact">Compact</option>
                  <option value="comfortable">Comfortable</option>
                  <option value="spacious">Spacious</option>
                </select>
              </label>
              <label className="studio-field">
                Interface size <span className="studio-value">{appearance.scale ?? 100}%</span>
                <input
                  type="range"
                  min="85"
                  max="130"
                  step="5"
                  value={appearance.scale ?? 100}
                  onChange={(event) => appearanceChange({ scale: Number(event.target.value) })}
                />
              </label>
              <label className="studio-field">
                Corners <span className="studio-value">{appearance.radius}px</span>
                <input
                  type="range"
                  min="0"
                  max="32"
                  step="1"
                  value={appearance.radius}
                  onChange={(event) => appearanceChange({ radius: Number(event.target.value) })}
                />
              </label>
              <label className="studio-field">
                Artwork shade <span className="studio-value">{appearance.scrim}%</span>
                <input
                  type="range"
                  min="20"
                  max="90"
                  step="1"
                  value={appearance.scrim}
                  onChange={(event) => appearanceChange({ scrim: Number(event.target.value) })}
                />
                <small>A stronger shade makes text over artwork easier to read.</small>
              </label>
              <label className="studio-toggle">
                <input
                  type="checkbox"
                  checked={appearance.reducedMotion}
                  onChange={(event) => appearanceChange({ reducedMotion: event.target.checked })}
                />
                <span>
                  <strong>Reduce motion</strong>
                  <small>System accessibility preferences also apply.</small>
                </span>
              </label>
            </div>
            <details className="studio-custom-colors">
              <summary>
                Customize every color
                {appearance.colors && Object.keys(appearance.colors).length ? <span>Modified</span> : null}
              </summary>
              <p className="studio-help">
                Start with a preset, then set each color. Choosing a preset clears these overrides.
              </p>
              <div className="studio-field-grid">
                {colorLabels.map(({ key, label }) => (
                  <ColorControl
                    key={key}
                    label={label}
                    value={colors[key]}
                    onChange={(value) => appearanceChange({ colors: { ...appearance.colors, [key]: value } })}
                    onReset={() => resetColor(key)}
                  />
                ))}
              </div>
            </details>
            {!!contrastIssues.length && (
              <div className="studio-warning" role="status">
                <strong>Some colors may be hard to read.</strong>
                <p>Use at least 4.5:1 contrast for small text.</p>
                <ul>
                  {contrastIssues.map((check) => (
                    <li key={check.label}>
                      {check.label}: {check.ratio.toFixed(1)}:1
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </section>
          <section className="studio-panel" aria-labelledby="studio-artwork">
            <div className="studio-section-title">
              <Sparkles size={18} />
              <h2 id="studio-artwork">Artwork materials</h2>
            </div>
            <p>
              These choices apply to themes and components that use artwork effects. Afterglow keeps its
              covers still and unlit; your choices stay saved for other themes.
            </p>
            <fieldset className="studio-fieldset">
              <legend>Surface finish</legend>
              <div className="studio-field-grid">
                <label className="studio-field">
                  Cover finish
                  <select
                    value={artwork.finish}
                    onChange={(event) =>
                      artworkChange({ finish: event.target.value as ArtworkEffectOptions['finish'] })
                    }
                  >
                    <option value="off">Off</option>
                    <option value="matte">Matte</option>
                    <option value="satin">Satin</option>
                    <option value="foil">Foil</option>
                  </select>
                </label>
                <label className="studio-field">
                  Finish intensity <span className="studio-value">{artwork.intensity}%</span>
                  <input
                    type="range"
                    min="0"
                    max="100"
                    step="1"
                    value={artwork.intensity}
                    disabled={artwork.finish === 'off'}
                    onChange={(event) => artworkChange({ intensity: Number(event.target.value) })}
                  />
                </label>
              </div>
            </fieldset>
            <fieldset className="studio-fieldset">
              <legend>Highlight foil</legend>
              <div className="studio-field-grid">
                <label className="studio-toggle">
                  <input
                    type="checkbox"
                    checked={artwork.highlightFoil}
                    onChange={(event) => artworkChange({ highlightFoil: event.target.checked })}
                  />
                  <span>
                    <strong>Foil on light areas</strong>
                    <small>Catch white lettering and bright details in the artwork.</small>
                  </span>
                </label>
                <label className="studio-field">
                  Highlight material
                  <select
                    value={artwork.foilMetal}
                    disabled={!artwork.highlightFoil}
                    onChange={(event) =>
                      artworkChange({ foilMetal: event.target.value as ArtworkEffectOptions['foilMetal'] })
                    }
                  >
                    <option value="silver">Silver</option>
                    <option value="gold">Gold</option>
                    <option value="holographic">Holographic</option>
                  </select>
                </label>
                <label className="studio-field">
                  Foil strength <span className="studio-value">{artwork.foilStrength}%</span>
                  <input
                    type="range"
                    min="0"
                    max="100"
                    step="1"
                    value={artwork.foilStrength}
                    disabled={!artwork.highlightFoil}
                    onChange={(event) => artworkChange({ foilStrength: Number(event.target.value) })}
                  />
                </label>
                <label className="studio-field">
                  Brightness cutoff <span className="studio-value">{artwork.foilThreshold}%</span>
                  <input
                    type="range"
                    min="40"
                    max="95"
                    step="1"
                    value={artwork.foilThreshold}
                    disabled={!artwork.highlightFoil}
                    onChange={(event) => artworkChange({ foilThreshold: Number(event.target.value) })}
                  />
                  <small>A higher cutoff limits foil to the brightest areas.</small>
                </label>
              </div>
            </fieldset>
            <fieldset className="studio-fieldset">
              <legend>Movement and depth</legend>
              {reducedMotion && (
                <div className="studio-notice studio-motion-status">
                  <span id={motionStatusId} role="status">
                    {systemReducedMotion ? 'Your system requests reduced motion.' : 'Reduce motion is on.'}{' '}
                    Tilt and cursor-following light are paused. Floating artwork can still lift.
                  </span>
                  {!systemReducedMotion && (
                    <button type="button" onClick={() => appearanceChange({ reducedMotion: false })}>
                      Turn off reduced motion
                    </button>
                  )}
                </div>
              )}
              <div className="studio-field-grid">
                <label className="studio-toggle">
                  <input
                    type="checkbox"
                    checked={artwork.followPointer}
                    disabled={reducedMotion}
                    aria-describedby={reducedMotion ? motionStatusId : undefined}
                    onChange={(event) => artworkChange({ followPointer: event.target.checked })}
                  />
                  <span>
                    <strong>Follow the pointer</strong>
                    <small>Move the light and card angle with your cursor.</small>
                  </span>
                </label>
                <label className="studio-toggle">
                  <input
                    type="checkbox"
                    checked={artwork.floating}
                    onChange={(event) => artworkChange({ floating: event.target.checked })}
                  />
                  <span>
                    <strong>Floating artwork</strong>
                    <small>Lift covers above the page with a soft shadow.</small>
                  </span>
                </label>
                <label className="studio-field">
                  Maximum tilt <span className="studio-value">{artwork.tilt}°</span>
                  <input
                    type="range"
                    min="0"
                    max="12"
                    step="1"
                    value={artwork.tilt}
                    disabled={reducedMotion || !artwork.followPointer || !artwork.floating}
                    aria-describedby={reducedMotion ? motionStatusId : undefined}
                    onChange={(event) => artworkChange({ tilt: Number(event.target.value) })}
                  />
                  <small>Set to zero for lift alone.</small>
                </label>
              </div>
              <p className="studio-help">Keyboard focus and reduced motion keep the card and light steady.</p>
            </fieldset>
          </section>
          {theme.id !== 'rift' && (
            <section className="studio-panel" aria-labelledby="studio-layout">
              <div className="studio-section-title">
                <LayoutTemplate size={18} />
                <h2 id="studio-layout">Arrange your space</h2>
              </div>
              <p>
                These preferences arrange Afterglow and shared screens. Other designs can provide their own
                controls below.
              </p>
              <div className="studio-field-grid">
                <label className="studio-field">
                  Navigation
                  <select
                    value={layout.navigation}
                    onChange={(event) =>
                      layoutChange({ navigation: event.target.value as ThemeProfile['layout']['navigation'] })
                    }
                  >
                    <option value="top">Across the top</option>
                    <option value="left">Along the left</option>
                  </select>
                </label>
                <label className="studio-field">
                  Library cards
                  <select
                    value={layout.cardStyle}
                    onChange={(event) =>
                      layoutChange({ cardStyle: event.target.value as ThemeProfile['layout']['cardStyle'] })
                    }
                  >
                    <option value="landscape">Landscape artwork</option>
                    <option value="poster">Portrait covers</option>
                    <option value="record">Compact records</option>
                  </select>
                </label>
                <label className="studio-field">
                  Game details
                  <select
                    value={layout.detailArrangement}
                    onChange={(event) =>
                      layoutChange({
                        detailArrangement: event.target.value as ThemeProfile['layout']['detailArrangement'],
                      })
                    }
                  >
                    <option value="aside">Actions beside the story</option>
                    <option value="stacked">One reading column</option>
                  </select>
                </label>
              </div>
              <fieldset className="studio-fieldset">
                <legend>Discover sections</legend>
                <p className="studio-help">Move sections or hide them. Keep at least one visible.</p>
                <ol className="studio-section-order">
                  {layout.discoverSections.map((section, index) => (
                    <li key={section}>
                      <label>
                        <input
                          type="checkbox"
                          checked={!layout.hiddenSections.includes(section)}
                          disabled={
                            !layout.hiddenSections.includes(section) && layout.hiddenSections.length === 2
                          }
                          onChange={(event) =>
                            layoutChange({
                              hiddenSections: event.target.checked
                                ? layout.hiddenSections.filter((item) => item !== section)
                                : [...layout.hiddenSections, section],
                            })
                          }
                        />
                        <span>{sectionLabels[section]}</span>
                      </label>
                      <div>
                        <button
                          type="button"
                          disabled={index === 0}
                          onClick={() => moveSection(index, -1)}
                          aria-label={`Move ${sectionLabels[section].toLowerCase()} up`}
                        >
                          <ArrowUp size={16} />
                        </button>
                        <button
                          type="button"
                          disabled={index === layout.discoverSections.length - 1}
                          onClick={() => moveSection(index, 1)}
                          aria-label={`Move ${sectionLabels[section].toLowerCase()} down`}
                        >
                          <ArrowDown size={16} />
                        </button>
                      </div>
                    </li>
                  ))}
                </ol>
              </fieldset>
            </section>
          )}
          {!!theme.settings?.length && (
            <section className="studio-panel" aria-labelledby="studio-extra">
              <h2 id="studio-extra">{theme.name} details</h2>
              {theme.id === 'rift' && reducedMotion && <p>Reduced motion keeps the portal edge still.</p>}
              <div className="studio-field-grid">
                {theme.settings.map((field) => (
                  <label
                    className={field.type === 'toggle' ? 'studio-toggle' : 'studio-field'}
                    key={field.id}
                  >
                    {field.type === 'toggle' ? (
                      <>
                        <input
                          type="checkbox"
                          checked={Boolean(themeSettings[field.id])}
                          onChange={(event) => settingChange(field.id, event.target.checked)}
                        />
                        <span>
                          {field.label}
                          {field.description && <small>{field.description}</small>}
                        </span>
                      </>
                    ) : (
                      <>
                        {field.label}
                        {field.type === 'select' ? (
                          <select
                            value={String(themeSettings[field.id])}
                            onChange={(event) => settingChange(field.id, event.target.value)}
                          >
                            {field.options.map((option) => (
                              <option key={option.value} value={option.value}>
                                {option.label}
                              </option>
                            ))}
                          </select>
                        ) : (
                          <>
                            <span className="studio-value">{String(themeSettings[field.id])}</span>
                            <input
                              type="range"
                              min={field.min}
                              max={field.max}
                              step={field.step ?? 1}
                              disabled={theme.id === 'rift' && field.id === 'portalActivity' && reducedMotion}
                              value={Number(themeSettings[field.id])}
                              onChange={(event) => settingChange(field.id, Number(event.target.value))}
                            />
                          </>
                        )}
                        {field.description && <small>{field.description}</small>}
                      </>
                    )}
                  </label>
                ))}
              </div>
            </section>
          )}
        </div>
        <aside className="studio-preview" aria-label="Live appearance sample">
          <div className="studio-preview-top">
            <span>Live appearance</span>
            <span className="studio-preview-dot" />
          </div>
          {theme.id === 'rift' && (
            <RiftStudioPreview settings={themeSettings} reducedMotion={reducedMotion} />
          )}
          <div className="studio-specimen" hidden={theme.id === 'rift'}>
            <p className="studio-kicker">A little room to wander</p>
            <h2>
              A library,
              <br />
              made yours.
            </h2>
            <p>Old favorites. New possibilities. A familiar place to find what comes next.</p>
            <div className="studio-sample-card">
              <span className="studio-sample-art" aria-hidden="true" />
              <div>
                <strong>Your next good evening</strong>
                <span>Waiting in your library</span>
              </div>
              <Check size={17} />
            </div>
            <span className="studio-preview-button" aria-hidden="true">
              Your next good evening
            </span>
            <p className="studio-sample-caption">Changes apply as you make them.</p>
          </div>
          <div className="studio-preview-foot">
            <span>{theme.id === 'rift' ? 'COLOR / SHAPE / MOTION' : 'TYPE / COLOR / SPACE'}</span>
            <span>01</span>
          </div>
        </aside>
      </div>
    </div>
  )
}

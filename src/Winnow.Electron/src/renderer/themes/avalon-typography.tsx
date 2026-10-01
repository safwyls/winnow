import { useEffect, useId, useState } from 'react'
import { DEFAULT_TYPOGRAPHY, validFontFamily, type ThemeTypography } from '../../shared/typography'
import { resolvedTypography, typographyKey, type ThemeProfile } from '../../shared/theme'

const roles = [
  ['headingFont', 'Heading font'],
  ['interfaceFont', 'Interface font'],
  ['dataFont', 'Data font'],
] as const

function FontChoice({
  label,
  value,
  choices,
  onChange,
}: {
  label: string
  value: string
  choices: string[]
  onChange(value: string): void
}) {
  const id = useId()
  const canonical =
    choices.find((choice) => choice.toLocaleLowerCase() === value.toLocaleLowerCase()) ?? value
  const [draft, setDraft] = useState(canonical)
  useEffect(() => setDraft(canonical), [canonical])
  const invalid = !validFontFamily(draft)
  return (
    <label className="studio-field">
      {label}
      <input
        aria-label={label}
        list={id}
        value={draft}
        maxLength={128}
        aria-invalid={invalid}
        onChange={(event) => {
          setDraft(event.target.value)
          if (validFontFamily(event.target.value)) onChange(event.target.value)
        }}
        onBlur={() => {
          if (invalid) setDraft(canonical)
        }}
      />
      <datalist id={id}>
        {choices.map((name) => (
          <option key={name} value={name}>
            {name}
          </option>
        ))}
      </datalist>
      {invalid && <small role="alert">Enter a single font family name.</small>}
    </label>
  )
}

export function AvalonTypographyControls({
  profile,
  onChange,
}: {
  profile: ThemeProfile
  onChange(value: ThemeProfile['appearance']['typography']): void
}) {
  const current = resolvedTypography(profile),
    key = typographyKey(profile)
  const [fonts, setFonts] = useState<string[]>(
    Object.values(DEFAULT_TYPOGRAPHY).filter((value): value is string => typeof value === 'string'),
  )
  const [finding, setFinding] = useState(false)
  const [notice, setNotice] = useState('')
  const change = (patch: Partial<ThemeTypography>) =>
    onChange({ ...profile.appearance.typography, [key]: { ...current, ...patch } })
  const findFonts = async () => {
    setFinding(true)
    try {
      const available = await window.winnow.listFonts?.()
      if (!available?.length)
        setNotice('Type an installed font family name. Unavailable fonts use the bundled font for that role.')
      else {
        setFonts(
          [
            ...new Map(
              [...fonts, ...available.filter(validFontFamily)].map((name) => [
                name.toLocaleLowerCase(),
                name,
              ]),
            ).values(),
          ].sort((a, b) => a.localeCompare(b)),
        )
        setNotice(`${available.length.toLocaleString()} installed font families available.`)
      }
    } catch {
      setNotice('Installed fonts could not be listed. You can still type a family name.')
    } finally {
      setFinding(false)
    }
  }
  return (
    <section className="studio-panel" aria-labelledby="avalon-typography-heading">
      <h2 id="avalon-typography-heading">Theme typography</h2>
      <p className="reading-prose">
        Fonts and text size follow this Avalon palette. Unavailable fonts use the bundled font for that role.
      </p>
      <div className="studio-field-grid">
        {roles.map(([role, label]) => (
          <FontChoice
            key={`${key}:${role}`}
            label={label}
            value={current[role]}
            choices={fonts}
            onChange={(value) => change({ [role]: value })}
          />
        ))}
        <label className="studio-field">
          Theme text size
          <span className="studio-value">{current.sizePercent}%</span>
          <input
            aria-label="Theme text size"
            type="range"
            min={80}
            max={120}
            step={5}
            value={current.sizePercent}
            onChange={(event) =>
              change({ sizePercent: Math.round(Math.max(80, Math.min(120, Number(event.target.value)))) })
            }
          />
        </label>
      </div>
      <div className="inline-actions">
        <button onClick={findFonts} disabled={finding}>
          {finding ? 'Finding fonts…' : 'Find installed fonts'}
        </button>
        <button
          onClick={() => {
            const remaining = { ...profile.appearance.typography }
            delete remaining[key]
            onChange(remaining)
          }}
        >
          Reset theme typography
        </button>
      </div>
      {notice && <p role="status">{notice}</p>}
    </section>
  )
}

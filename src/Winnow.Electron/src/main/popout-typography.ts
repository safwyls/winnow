import {
  DEFAULT_TYPOGRAPHY,
  fontFamilyStack,
  parseTypography,
  type ThemeTypography,
} from '../shared/typography'
import displayFont from '../renderer/themes/avalon/assets/BricolageGrotesque-Bold.ttf?inline'
import bodyFont from '../renderer/themes/avalon/assets/PlusJakartaSans-Regular.ttf?inline'
import dataFont from '../renderer/themes/avalon/assets/IBMPlexMono-Regular.ttf?inline'

interface Owner {
  isDestroyed(): boolean
  once(event: 'destroyed', listener: () => void): unknown
}
type Listener = (typography: ThemeTypography) => void
interface State {
  value: ThemeTypography
  listeners: Set<Listener>
}
const owners = new WeakMap<Owner, State>()

function state(owner: Owner): State {
  let current = owners.get(owner)
  if (!current) {
    current = { value: { ...DEFAULT_TYPOGRAPHY }, listeners: new Set() }
    owners.set(owner, current)
    const registered = current
    owner.once('destroyed', () => {
      registered.listeners.clear()
      owners.delete(owner)
    })
  }
  return current
}

/** Called only behind the application's existing top-level sender validation. */
export function setPopoutTypography(owner: Owner, value: unknown): void {
  const typography = parseTypography(value)
  if (owner.isDestroyed()) return
  const current = state(owner)
  if (
    Object.keys(DEFAULT_TYPOGRAPHY).every(
      (key) => current.value[key as keyof ThemeTypography] === typography[key as keyof ThemeTypography],
    )
  )
    return
  current.value = typography
  for (const listener of current.listeners) listener({ ...typography })
}

export function popoutTypography(owner: Owner): ThemeTypography {
  return { ...(owner.isDestroyed() ? DEFAULT_TYPOGRAPHY : state(owner).value) }
}

/** Only locally authored toolbar/composer documents register listeners. */
export function subscribePopoutTypography(owner: Owner, listener: Listener): () => void {
  if (owner.isDestroyed()) return () => {}
  const current = state(owner)
  current.listeners.add(listener)
  return () => current.listeners.delete(listener)
}

// Fonts are build-time imports of known bundled files. No theme-supplied URL is permitted.
export const popoutFontFaces = [
  ['Avalon Display', displayFont, 700],
  ['Avalon Body', bodyFont, 400],
  ['Avalon Data', dataFont, 400],
]
  .map(
    ([family, url, weight]) =>
      `@font-face{font-family:"${family}";src:url("${url}") format("truetype");font-weight:${weight};font-display:swap}`,
  )
  .join('')

export function popoutTypographyValues(value: ThemeTypography): Record<string, string> {
  const typography = parseTypography(value)
  return {
    '--theme-text-scale': String(typography.sizePercent / 100),
    '--font-display': fontFamilyStack(typography.headingFont, 'headingFont'),
    '--font-body': fontFamilyStack(typography.interfaceFont, 'interfaceFont'),
    '--font-mono': fontFamilyStack(typography.dataFont, 'dataFont'),
  }
}

/** Changes only local presentation; existing inputs, their drafts and focus are retained. */
export function popoutTypographyScript(value: ThemeTypography): string {
  return `(()=>{const values=${JSON.stringify(popoutTypographyValues(value))};for(const [key,value] of Object.entries(values))document.documentElement.style.setProperty(key,value)})()`
}

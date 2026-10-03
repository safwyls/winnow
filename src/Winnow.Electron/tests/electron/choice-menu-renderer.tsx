import '../../src/renderer/styles.css'
import '@fontsource/dm-sans/400.css'
import '@fontsource/dm-sans/500.css'
import '@fontsource/dm-sans/600.css'
import '@fontsource/newsreader/400.css'
import '@fontsource/jetbrains-mono/400.css'
import '../../src/renderer/themes/avalon.css'
import '../../src/renderer/themes/rift/rift.css'
import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import * as Dialog from '@radix-ui/react-dialog'
import { ChoiceMenu, type ChoiceOption } from '../../src/renderer/components/ChoiceMenu'
import { AvalonActions } from '../../src/renderer/themes/avalon-actions'
import { avalon } from '../../src/renderer/themes/avalon'
import { afterglow } from '../../src/renderer/themes/afterglow'
import { rift } from '../../src/renderer/themes/rift'
import { catalogue } from '../../src/renderer/themes/catalogue'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeMode } from '../../src/shared/theme'
import { applyThemeProfile } from '../../src/renderer/theming/runtime'

type Configuration = { theme: string; mode: ThemeMode; zoom: number }
const options: ChoiceOption[] = [
  { value: 'steam', label: 'Steam' },
  { value: 'unavailable', label: 'PlayStation (unavailable)', disabled: true },
  { value: 'gog', label: 'GOG' },
  ...Array.from({ length: 60 }, (_, index) => ({
    value: `provider-${index}`,
    label: `Library provider ${String(index + 1).padStart(2, '0')} — a deliberately long provider name that wraps within the menu`,
  })),
  { value: 'last', label: 'Z-store' },
]

let updateConfiguration: (configuration: Configuration) => void = () => {}
function configure(configuration: Configuration) {
  const theme = [avalon, afterglow, rift, catalogue].find((theme) => theme.id === configuration.theme)!
  const profile = selectThemeProfile(structuredClone(DEFAULT_PROFILE), theme.id, theme)
  profile.appearance.reducedMotion = true
  applyThemeProfile(profile)
  document.documentElement.dataset.mode = configuration.mode
  document.documentElement.style.setProperty('--effective-interface-scale', String(configuration.zoom))
  updateConfiguration(configuration)
}
Object.assign(window, { choiceMenuProbe: { configure } })
configure({ theme: 'avalon', mode: 'desktop', zoom: 1 })

function StoreField({ label }: { label: string }) {
  const [value, setValue] = useState('steam')
  return (
    <div className="choice-probe-field">
      <button type="button">Before {label}</button>
      <ChoiceMenu label={label} aria-label={label} value={value} options={options} onChange={setValue} />
      <button type="button">After {label}</button>
      <output aria-label={`${label} saved value`}>{value}</output>
    </div>
  )
}

function NestedDialog() {
  return (
    <Dialog.Root>
      <Dialog.Trigger asChild>
        <button type="button">Open nested dialog</button>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content className="dialog-content choice-probe-dialog" aria-describedby={undefined}>
          <Dialog.Title>Nested preferences</Dialog.Title>
          <StoreField label="Nested store" />
          <Dialog.Close asChild>
            <button type="button">Close nested dialog</button>
          </Dialog.Close>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

function Fixture({ configuration }: { configuration: Configuration }) {
  const [open, setOpen] = useState(false)
  const content = (
    <>
      <StoreField label="Dialog store" />
      <NestedDialog />
    </>
  )
  return (
    <div
      className={`${configuration.theme === 'avalon' ? 'avalon-shell' : `app-shell ${configuration.theme}-shell`} ${configuration.mode} choice-probe-shell`}
      data-fixture-theme={configuration.theme}
      data-fixture-mode={configuration.mode}
      data-fixture-zoom={configuration.zoom}
    >
      <main>
        <h1>Choice menu fixture ready</h1>
        <p>Library and dialog choices use the production menu and composition palette.</p>
        <Dialog.Root open={open} onOpenChange={setOpen}>
          <Dialog.Trigger asChild>
            <button type="button">Open preferences</button>
          </Dialog.Trigger>
          {configuration.mode === 'desktop' && (
            <Dialog.Portal>
              <Dialog.Overlay className="dialog-overlay" />
              <Dialog.Content className="dialog-content choice-probe-dialog" aria-describedby={undefined}>
                <Dialog.Title>Preferences</Dialog.Title>
                {content}
                <Dialog.Close asChild>
                  <button type="button">Close preferences</button>
                </Dialog.Close>
              </Dialog.Content>
            </Dialog.Portal>
          )}
        </Dialog.Root>
        <div className="choice-probe-clipped-edge">
          <StoreField label="Library store" />
        </div>
      </main>
      {configuration.mode === 'fullscreen' && (
        <AvalonActions open={open} title="Preferences" close={() => setOpen(false)}>
          {content}
        </AvalonActions>
      )}
      <style>{`
        .choice-probe-shell { display: block; position: relative; width: 100%; height: 100%; padding: 32px; }
        .choice-probe-field { display: grid; gap: 12px; min-width: 0; }
        .choice-probe-dialog { display: grid; gap: 20px; }
        .choice-probe-clipped-edge { position: absolute; bottom: 18px; right: 18px; width: 500px; max-width: calc(100% - 36px); overflow: hidden; transform: translateZ(0); }
        .choice-probe-clipped-edge .choice-probe-field { grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; }
        .choice-probe-clipped-edge output { grid-column: 1 / -1; }
      `}</style>
    </div>
  )
}

function App() {
  const [configuration, setConfiguration] = useState<Configuration>({
    theme: 'avalon',
    mode: 'desktop',
    zoom: 1,
  })
  updateConfiguration = setConfiguration
  return (
    <Fixture
      key={`${configuration.theme}-${configuration.mode}-${configuration.zoom}`}
      configuration={configuration}
    />
  )
}
createRoot(document.getElementById('root')!).render(<App />)

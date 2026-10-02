import { useLayoutEffect, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { ChevronDown, X } from 'lucide-react'
import { useApiQuery } from '../api/hooks'
import type { LibraryPreferences } from '../api/types'
import { JournalPromptPreference, usePresentationPreferences } from './SettingsPreferences'
import { RatingCapPreference, useLibraryPreferenceChange } from './RatingCap'
import { parseExpansionGrouping } from './parity-library-grain'
import { Notice } from './shared'
import './display-preferences.css'

export function DisplayPreferences() {
  const [open, setOpen] = useState(false)
  const anchor = useRef<HTMLButtonElement>(null)
  const [panel, setPanel] = useState<HTMLDivElement | null>(null)
  const [position, setPosition] = useState({ left: 18, top: 72 })
  useLayoutEffect(() => {
    if (!open || !panel) return
    const place = () => {
      const bounds = anchor.current!.getBoundingClientRect()
      const height = panel.scrollHeight + panel.offsetHeight - panel.clientHeight
      setPosition({
        left: Math.max(18, Math.min(bounds.right - 460, window.innerWidth - 478)),
        top: Math.max(18, Math.min(bounds.bottom + 8, window.innerHeight - height - 18)),
      })
    }
    place()
    window.addEventListener('resize', place)
    const observer = new ResizeObserver(place)
    observer.observe(panel)
    return () => {
      window.removeEventListener('resize', place)
      observer.disconnect()
    }
  }, [open, panel])
  return (
    <Dialog.Root open={open} onOpenChange={setOpen} modal={false}>
      <Dialog.Trigger asChild>
        <button ref={anchor} aria-label="Display preferences" title="Display preferences">
          Display <ChevronDown size={12} />
        </button>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Content
          ref={setPanel}
          className="display-preferences-popover"
          style={{ ...position, maxHeight: `calc(100dvh - ${position.top + 18}px)` }}
        >
          <header>
            <Dialog.Title>Display preferences</Dialog.Title>
            <Dialog.Close aria-label="Close display preferences">
              <X size={16} />
            </Dialog.Close>
          </header>
          <Dialog.Description className="display-preferences-description">
            Adjust the games and covers shown in your library.
          </Dialog.Description>
          <DisplayPreferenceToggles />
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

function DisplayPreferenceToggles() {
  const preferences = usePresentationPreferences()
  const library = useApiQuery<LibraryPreferences>('preferences.library.get')
  const change = useLibraryPreferenceChange()
  return (
    <>
      <label className="field">
        Cover art
        <select
          aria-label="Cover art"
          value={preferences.values.CoverArtMode === 'fill' ? 'fill' : 'fit'}
          disabled={!preferences.loaded || preferences.pending}
          onChange={(event) => preferences.set('CoverArtMode', event.target.value)}
        >
          <option value="fit">Fit — show entire artwork</option>
          <option value="fill">Fill — crop to card</option>
        </select>
      </label>
      <label className="check-field">
        <input
          type="checkbox"
          checked={preferences.values.DimDormantCovers?.trim().toLowerCase() !== 'false'}
          disabled={!preferences.loaded || preferences.pending}
          onChange={(event) => preferences.set('DimDormantCovers', String(event.target.checked))}
        />
        Dim dormant covers
      </label>
      <p className="display-preference-help">Covers fade the longer a game sits unplayed.</p>
      <label className="check-field">
        <input
          type="checkbox"
          checked={library.data?.showNonGameEntries ?? false}
          disabled={!library.data || change.pending}
          onChange={(event) => {
            void change.apply('showNonGameEntries', event.target.checked)
          }}
        />
        Show non-game entries
      </label>
      <p className="display-preference-help">Tools, servers and soundtracks. Hidden by default.</p>
      <RatingCapPreference />
      <label className="check-field">
        <input
          type="checkbox"
          checked={parseExpansionGrouping(preferences.values.GroupExpansions)}
          disabled={!preferences.loaded || preferences.pending}
          onChange={(event) => preferences.set('GroupExpansions', String(event.target.checked))}
        />
        Group expansions under the base game
      </label>
      <p className="display-preference-help">Off by default. Hours are never added together.</p>
      <Notice error={preferences.error || library.error || change.error} />
      <JournalPromptPreference />
    </>
  )
}

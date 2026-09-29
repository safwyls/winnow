import { createContext, useContext, useState, type ReactNode } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useSteamAccountBusy } from './SteamAccountOperation'
import { useSetupBusy } from './settingsState'

type SteamModal = 'methods' | 'accounts' | 'purchase' | 'consent'
const Context = createContext<{ modal: SteamModal | null; set(value: SteamModal | null): void } | null>(null)

export function SteamModals({ children }: { children: ReactNode }) {
  const [modal, set] = useState<SteamModal | null>(null)
  useSetupBusy(modal !== null)
  return <Context.Provider value={{ modal, set }}>{children}</Context.Provider>
}

export function useSteamModal(name: SteamModal): [boolean, (value: boolean) => void] {
  const shared = useContext(Context)
  const [local, setLocal] = useState(false)
  return shared ? [shared.modal === name, (open) => shared.set(open ? name : null)] : [local, setLocal]
}

export function SteamInformationDialog({
  name,
  label,
  title,
  description,
  children,
}: {
  name: Exclude<SteamModal, 'consent'>
  label: string
  title: string
  description: string
  children: ReactNode
}) {
  const [open, setOpen] = useSteamModal(name)
  const busy = useSteamAccountBusy()
  return (
    <Dialog.Root
      open={open}
      onOpenChange={(value) => {
        if (!busy) setOpen(value)
      }}
    >
      <Dialog.Trigger asChild>
        <button disabled={busy}>{label}</button>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content
          className="dialog-content feature-panel steam-information-dialog"
          onEscapeKeyDown={(event) => {
            if (busy) event.preventDefault()
          }}
          onPointerDownOutside={(event) => event.preventDefault()}
        >
          <Dialog.Title>{title}</Dialog.Title>
          <Dialog.Description>{description}</Dialog.Description>
          {children}
          <Dialog.Close asChild>
            <button disabled={busy}>Close</button>
          </Dialog.Close>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

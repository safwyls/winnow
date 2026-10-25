import { useState } from 'react'
import { FolderOpen } from 'lucide-react'
import { Notice } from './shared'

export function InstallFolderButton({
  ownershipId,
  installed,
  installPath,
}: {
  ownershipId: number
  installed?: boolean | null
  installPath?: string | null
}) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<unknown>(null)
  if (!installed || !installPath?.trim()) return null
  async function open() {
    if (pending) return
    setPending(true)
    setError(null)
    try {
      if (!window.winnow.openInstallFolder)
        throw new Error('Opening folders is unavailable in this frontend.')
      await window.winnow.openInstallFolder(ownershipId)
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  return (
    <div className="install-folder-action">
      <button type="button" disabled={pending} onClick={() => void open()}>
        <FolderOpen size={16} aria-hidden="true" /> Open install folder
      </button>
      <Notice error={error} />
    </div>
  )
}

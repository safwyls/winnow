import { createContext, useContext } from 'react'
import type { ThemeContext } from '../../../shared/theme'

export interface JourneyOrigin {
  workId: number
  rect: DOMRect | null
  page: string
}
export interface RiftJourney {
  origin: React.RefObject<JourneyOrigin | null>
  open(workId: number, source?: HTMLElement | DOMRect | null): void
  finish(): void
  options: { roundness: number; waviness: number; activity: number }
  reducedMotion: boolean
  coverSize: string
}
export const JourneyContext = createContext<RiftJourney | null>(null)
export function useJourney(): RiftJourney {
  const value = useContext(JourneyContext)
  if (!value) throw Error('Rift screens need the Rift shell.')
  return value
}
export const storeNames = (game: ThemeContext['games'][number]) =>
  [...new Set(game.entries.map((entry) => entry.store))].join(' / ')

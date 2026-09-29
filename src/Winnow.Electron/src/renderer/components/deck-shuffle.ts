interface CardPose {
  transform: string
  opacity: string
  slot: string | undefined
}
export type DeckPoses = Map<string, CardPose>

/** Animate the outer card; artwork lighting and pointer tilt remain on its inner surface. */
export class DeckShuffle {
  private animations = new Set<Animation>()
  private layers = new Map<HTMLElement, { value: string; priority: string }>()
  private root: HTMLElement | null = null

  capture(root: HTMLElement): DeckPoses {
    return new Map(
      [...root.querySelectorAll<HTMLElement>('[data-deck-key]')].map((card) => {
        const style = getComputedStyle(card)
        return [
          card.dataset.deckKey!,
          {
            transform: style.transform,
            opacity: style.opacity,
            slot: card.dataset.deckSlot,
          },
        ]
      }),
    )
  }

  play(root: HTMLElement, before: DeckPoses, direction: number, reducedMotion = false): void {
    // Capturing before cancellation lets repeated input continue from the current pose.
    this.stop()
    if (reducedMotion || !root.animate || before.size < 2) return
    this.root = root
    root.dataset.shuffling = 'true'
    const sign = direction > 0 ? 1 : -1
    for (const card of root.querySelectorAll<HTMLElement>('[data-deck-key]')) {
      const previous = before.get(card.dataset.deckKey!)
      const style = getComputedStyle(card)
      const end = { transform: style.transform, opacity: style.opacity }
      // Keep stacking out of keyframes so the moving properties can be composited.
      this.layers.set(card, {
        value: card.style.getPropertyValue('z-index'),
        priority: card.style.getPropertyPriority('z-index'),
      })
      card.style.zIndex =
        previous?.slot === 'selected' ? '4' : card.dataset.deckSlot === 'selected' ? '5' : '0'
      let frames: Keyframe[]
      if (previous?.slot === 'selected') {
        frames = [
          { transform: previous.transform, opacity: previous.opacity, offset: 0 },
          {
            transform: `translateX(${sign > 0 ? '-115%' : '15%'}) translateY(-14px) rotate(${-sign * 19}deg) scale(.94)`,
            opacity: 0.92,
            offset: 0.4,
          },
          { ...end, offset: 1 },
        ]
      } else if (card.dataset.deckSlot === 'selected') {
        frames = [
          {
            transform:
              previous?.transform ??
              `translateX(${sign > 0 ? '-12%' : '-84%'}) translateY(22px) rotate(${sign * 12}deg) scale(.82)`,
            opacity: previous?.opacity ?? 0.6,
            offset: 0,
          },
          {
            transform: `translateX(${sign > 0 ? '-42%' : '-58%'}) translateY(-10px) rotate(${sign * 3}deg) scale(1.015)`,
            opacity: 1,
            offset: 0.62,
          },
          { ...end, offset: 1 },
        ]
      } else {
        frames = [
          {
            transform: previous?.transform ?? 'translateX(-50%) translateY(30px) scale(.74)',
            opacity: previous?.opacity ?? 0,
          },
          end,
        ]
      }
      const animation = card.animate(frames, { duration: 520, easing: 'cubic-bezier(.22,.7,.2,1)' })
      this.animations.add(animation)
      animation.onfinish = () => {
        this.animations.delete(animation)
        this.restoreLayer(card)
        if (!this.animations.size) {
          delete root.dataset.shuffling
          this.root = null
        }
      }
    }
  }

  private restoreLayer(card: HTMLElement): void {
    const layer = this.layers.get(card)
    if (!layer) return
    if (layer.value) card.style.setProperty('z-index', layer.value, layer.priority)
    else card.style.removeProperty('z-index')
    this.layers.delete(card)
  }

  stop(): void {
    for (const animation of this.animations) {
      animation.onfinish = null
      animation.cancel()
    }
    this.animations.clear()
    for (const card of this.layers.keys()) this.restoreLayer(card)
    if (this.root) delete this.root.dataset.shuffling
    this.root = null
  }
}

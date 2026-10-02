type Exposure = { releaseId: number; shelfId: string; rectangle?: DOMRectReadOnly }
type Report = (releaseId: number, shelfId: string) => Promise<unknown>

/** One observer set per document; the daily ledger survives card and screen remounts. */
export class FeedImpressions {
  private cards = new Map<HTMLElement, Exposure>()
  private observed = new Set<number>()
  private day = ''
  private intersections?: IntersectionObserver
  private mutations?: MutationObserver
  private frame?: number
  private midnight?: ReturnType<typeof setTimeout>

  constructor(
    private document: Document,
    private report: Report,
    private now = () => Date.now(),
  ) {}

  observe(element: HTMLElement, releaseId: number, shelfId: string) {
    if (!Number.isSafeInteger(releaseId) || releaseId <= 0 || /^recently[-_]played$/.test(shelfId))
      return () => {}
    this.start()
    this.cards.set(element, { releaseId, shelfId })
    this.intersections?.observe(element)
    let active = true
    return () => {
      if (!active) return
      active = false
      this.cards.delete(element)
      this.intersections?.unobserve(element)
      if (!this.cards.size) this.stop()
    }
  }

  private start() {
    if (this.intersections) return
    const observer = new IntersectionObserver(
      (entries) => {
        // Disconnected observers may still have callbacks queued for an old screen.
        if (this.intersections !== observer) return
        for (const entry of entries) {
          const card = this.cards.get(entry.target as HTMLElement)
          if (card)
            card.rectangle =
              entry.isIntersecting && entry.intersectionRatio > 0 ? entry.intersectionRect : undefined
        }
        this.refresh()
      },
      { threshold: [0, Number.EPSILON] },
    )
    this.intersections = observer
    this.mutations = new MutationObserver(this.schedule)
    this.mutations.observe(this.document.documentElement, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['class', 'style', 'hidden', 'aria-hidden', 'inert', 'open', 'role', 'data-state'],
    })
    this.document.defaultView?.addEventListener('focus', this.schedule)
    this.document.addEventListener('visibilitychange', this.schedule)
    this.document.addEventListener('transitionend', this.schedule, true)
    this.document.addEventListener('animationend', this.schedule, true)
    this.scheduleMidnight()
  }

  private schedule = () => {
    const window = this.document.defaultView
    if (!window || this.frame !== undefined || !this.cards.size) return
    this.frame = window.requestAnimationFrame(() => {
      this.frame = undefined
      this.refresh()
    })
  }

  private scheduleMidnight() {
    const current = new Date(this.now())
    const next = Date.UTC(current.getUTCFullYear(), current.getUTCMonth(), current.getUTCDate() + 1)
    this.midnight = setTimeout(
      () => {
        this.refresh()
        if (this.cards.size) this.scheduleMidnight()
      },
      Math.max(1, next - current.getTime() + 1),
    )
  }

  private visible(element: HTMLElement) {
    if (
      !element.isConnected ||
      element.ownerDocument !== this.document ||
      element.closest('[hidden], [aria-hidden="true"], [inert]')
    )
      return false
    if (typeof element.checkVisibility === 'function')
      return element.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true })
    for (let parent: HTMLElement | null = element; parent; parent = parent.parentElement) {
      const style = this.document.defaultView?.getComputedStyle(parent)
      if (
        style?.display === 'none' ||
        style?.visibility === 'hidden' ||
        style?.visibility === 'collapse' ||
        style?.opacity === '0'
      )
        return false
    }
    return true
  }

  private refresh() {
    if (this.document.visibilityState !== 'visible' || !this.document.hasFocus()) return
    if (
      [...this.document.querySelectorAll<HTMLElement>('[role="dialog"], [role="alertdialog"]')].some(
        (element) => this.visible(element),
      )
    )
      return
    const day = new Date(this.now()).toISOString().slice(0, 10)
    if (day !== this.day) {
      this.day = day
      this.observed.clear()
    }
    for (const [element, card] of this.cards) {
      if (!card.rectangle || this.observed.has(card.releaseId) || !this.visible(element)) continue
      // Intersections account for scroll clipping; hit testing also rejects opaque overlays.
      const rectangle = card.rectangle
      if (typeof this.document.elementFromPoint === 'function') {
        const hit = this.document.elementFromPoint(
          rectangle.x + rectangle.width / 2,
          rectangle.y + rectangle.height / 2,
        )
        if (!hit || !element.contains(hit)) continue
      }
      this.observed.add(card.releaseId)
      try {
        void this.report(card.releaseId, card.shelfId).catch(() => {
          if (this.day === day) this.observed.delete(card.releaseId)
        })
      } catch {
        if (this.day === day) this.observed.delete(card.releaseId)
      }
    }
  }

  private stop() {
    this.intersections?.disconnect()
    this.intersections = undefined
    this.mutations?.disconnect()
    this.mutations = undefined
    this.document.defaultView?.removeEventListener('focus', this.schedule)
    this.document.removeEventListener('visibilitychange', this.schedule)
    this.document.removeEventListener('transitionend', this.schedule, true)
    this.document.removeEventListener('animationend', this.schedule, true)
    if (this.frame !== undefined) this.document.defaultView?.cancelAnimationFrame(this.frame)
    this.frame = undefined
    clearTimeout(this.midnight)
    this.midnight = undefined
  }
}

const documents = new WeakMap<Document, FeedImpressions>()
export function observeFeedCard(element: HTMLElement, releaseId: number, shelfId: string) {
  let observer = documents.get(element.ownerDocument)
  if (!observer) {
    const owner = element.ownerDocument.defaultView
    if (!owner) return () => {}
    observer = new FeedImpressions(element.ownerDocument, async (releaseId, shelfId) => {
      const result = await owner.winnow.request({ route: 'feedImpression', body: { releaseId, shelfId } })
      if (!result.ok) throw Error('The impression could not be recorded.')
    })
    documents.set(element.ownerDocument, observer)
  }
  return observer.observe(element, releaseId, shelfId)
}

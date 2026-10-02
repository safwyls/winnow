import { expect, type Locator } from '@playwright/test'

export async function expectReadableDetails(details: Locator) {
  const failures = await details.evaluate((root) => {
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = 1
    const context = canvas.getContext('2d', { willReadFrequently: true })!
    const rgba = (css: string) => {
      context.clearRect(0, 0, 1, 1)
      context.fillStyle = css
      context.fillRect(0, 0, 1, 1)
      return [...context.getImageData(0, 0, 1, 1).data]
    }
    const composite = (ink: number[], ground: number[]) =>
      ground.slice(0, 3).map((value, i) => (ink[i] * ink[3]) / 255 + value * (1 - ink[3] / 255))
    const luminance = (color: number[]) =>
      color
        .slice(0, 3)
        .map((value) => value / 255)
        .map((value) => (value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4))
        .reduce((total, value, i) => total + value * [0.2126, 0.7152, 0.0722][i], 0)
    const tokens = getComputedStyle(root)
    const floor = rgba(tokens.getPropertyValue('--avalon-faint').trim()).join(',')
    // Match the source's flat-card ink floor. Artwork compositing has its own theme matrix.
    const ground = rgba(tokens.getPropertyValue('--surface').trim())
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
    const failures: { text: string; color: string; contrast: number }[] = []
    for (let text = walker.nextNode(); text; text = walker.nextNode()) {
      if (!text.textContent?.trim()) continue
      const node = text.parentElement!
      if (node.closest('[disabled],[aria-disabled="true"],[hidden],[inert],script,style')) continue
      const range = document.createRange()
      range.selectNode(text)
      if (!range.getBoundingClientRect().height || getComputedStyle(node).visibility !== 'visible') continue
      const ancestry: Element[] = []
      for (let parent: Element | null = node; parent; parent = parent === root ? null : parent.parentElement)
        ancestry.unshift(parent)
      let background = ground
      for (const parent of ancestry)
        background = [...composite(rgba(getComputedStyle(parent).backgroundColor), background), 255]
      const ink = rgba(getComputedStyle(node).color)
      const foregroundLight = luminance(composite(ink, background)),
        backgroundLight = luminance(background)
      const contrast =
        (Math.max(foregroundLight, backgroundLight) + 0.05) /
        (Math.min(foregroundLight, backgroundLight) + 0.05)
      if (ink.join(',') === floor || contrast < 4.49)
        failures.push({ text: text.textContent.trim(), color: getComputedStyle(node).color, contrast })
    }
    return failures
  })
  expect(failures, 'Enabled Details text must retain the original readable ink floor').toEqual([])
}

export async function expectReadingGutter(reading: Locator) {
  const gutter = await reading.evaluate((node) => {
    const style = getComputedStyle(node)
    return {
      gutter: style.scrollbarGutter,
      padding: parseFloat(style.paddingRight),
      horizontal: node.scrollWidth - node.clientWidth,
    }
  })
  expect(gutter.gutter).toBe('stable')
  expect(gutter.padding).toBeGreaterThanOrEqual(10)
  expect(gutter.horizontal).toBeLessThanOrEqual(1)
}

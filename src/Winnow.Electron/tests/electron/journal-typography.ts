import { expect, type Locator } from '@playwright/test'

export async function expectFullscreenJournalTypography(prompt: Locator, scale: number, error = false) {
  expect(
    await prompt
      .locator('.journal-controller-hints')
      .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
  ).toBeCloseTo(20 * scale)
  const roles = [
    { selector: '.journal-prompt-group h2', count: 3, size: 18, weight: '400' },
    { selector: '.journal-game-title', count: 1, size: 48, weight: '700' },
    { selector: '.journal-duration', count: 1, size: 22, weight: '400' },
    { selector: '.journal-current-rating', count: 1, size: 22, weight: '400' },
    ...(error ? [{ selector: '.error-message', count: 1, size: 24, weight: '400' }] : []),
  ]
  const bodyFamily = await prompt.evaluate((node) => {
    const probe = document.createElement('span')
    probe.style.fontFamily = 'var(--font-body)'
    node.append(probe)
    const family = getComputedStyle(probe).fontFamily
    probe.remove()
    return family
  })
  for (const role of roles) {
    const nodes = prompt.locator(role.selector)
    await expect(nodes).toHaveCount(role.count)
    for (const node of await nodes.all()) {
      const computed = await node.evaluate((element) => {
        const style = getComputedStyle(element)
        return { size: parseFloat(style.fontSize), weight: style.fontWeight, family: style.fontFamily }
      })
      expect(computed.size, `${role.selector} source size`).toBeCloseTo(role.size * scale)
      expect(computed.weight, `${role.selector} source weight`).toBe(role.weight)
      expect(computed.family, `${role.selector} body role`).toBe(bodyFamily)
    }
  }
}

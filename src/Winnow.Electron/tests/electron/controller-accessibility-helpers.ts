import { expect, test, type Locator, type Page } from '@playwright/test'

const controls = 'button, input:not([type="hidden"]), textarea, select, summary, a[href], [tabindex="0"]'
const attribute = 'data-accessibility-contract-id'

type Control = { id: string; disabled: boolean; button: boolean; label: string }

async function identify(scope: Locator): Promise<Control[]> {
  return scope.evaluate(
    (root, { controls, attribute }) => {
      const state = window as unknown as { accessibilityContractSequence?: number }
      return [...root.querySelectorAll<HTMLElement>(controls)]
        .filter((node) => {
          const rectangle = node.getBoundingClientRect()
          return (
            node.tabIndex >= 0 &&
            rectangle.width > 0 &&
            rectangle.height > 0 &&
            !node.closest('[inert], [hidden], [aria-hidden="true"]') &&
            node.checkVisibility({ checkVisibilityCSS: true })
          )
        })
        .map((node) => {
          if (!node.hasAttribute(attribute)) {
            state.accessibilityContractSequence = (state.accessibilityContractSequence ?? 0) + 1
            node.setAttribute(attribute, String(state.accessibilityContractSequence))
          }
          return {
            id: node.getAttribute(attribute)!,
            disabled: node.matches(':disabled, [aria-disabled="true"]'),
            button: node.matches('button, summary, [role="button"]'),
            label: node.getAttribute('aria-label') ?? node.textContent?.trim().slice(0, 120) ?? node.tagName,
          }
        })
    },
    { controls, attribute },
  )
}

/** Verify the exposed Chromium accessibility nodes, rather than infer names from DOM attributes. */
export async function assertAccessibleControls(page: Page, scope: Locator) {
  const candidates = await identify(scope)
  expect(candidates.length, 'The rendered surface has interactive controls').toBeGreaterThan(0)
  const session = await page.context().newCDPSession(page)
  try {
    await session.send('Accessibility.enable')
    const { root } = await session.send('DOM.getDocument')
    const result: { id: string; name: string; role: string; disabled: boolean }[] = []
    for (const control of candidates) {
      const { nodeId } = await session.send('DOM.querySelector', {
        nodeId: root.nodeId,
        selector: `[${attribute}="${control.id}"]`,
      })
      const { node } = await session.send('DOM.describeNode', { nodeId })
      const { nodes } = await session.send('Accessibility.getPartialAXTree', {
        backendNodeId: node.backendNodeId,
        fetchRelatives: false,
      })
      const exposed = nodes.find((entry) => entry.backendDOMNodeId === node.backendNodeId)
      expect(exposed, `${control.label} exists in Chromium's AX tree`).toBeDefined()
      expect(exposed!.ignored, `${control.label} is exposed to assistive technology`).toBe(false)
      expect(exposed!.name?.value?.trim(), `${control.label} has a meaningful accessible name`).toBeTruthy()
      const disabled =
        exposed!.properties?.find((property) => property.name === 'disabled')?.value.value === true
      expect(disabled, `${control.label} exposes its actual enabled state`).toBe(control.disabled)
      result.push({ id: control.id, name: exposed!.name!.value, role: exposed!.role!.value, disabled })
    }
    return result
  } finally {
    await session.send('Accessibility.disable')
    await session.send('DOM.disable')
    await session.detach()
  }
}

/** Explore actual D-pad routes from each reached control, including shell controls outside the page. */
export async function assertDirectionalReachability(
  page: Page,
  scope: Locator,
  tap: (button: number) => Promise<void>,
) {
  const expected = (await identify(scope)).filter((control) => control.button && !control.disabled)
  if (!expected.length) return
  await identify(page.locator('body'))
  const first = await scope.evaluate((root, attribute) => {
    const enabled = (node: Element | null): node is HTMLElement =>
      node instanceof HTMLElement &&
      node.hasAttribute(attribute) &&
      !node.matches(':disabled, [aria-disabled="true"]')
    const active = document.activeElement
    const initial =
      root.contains(active) && enabled(active)
        ? active
        : (root.querySelector<HTMLElement>(
            `[data-controller-initial][${attribute}], [data-initial-focus][${attribute}], [data-fullscreen-settings-initial][${attribute}]`,
          ) ?? [...root.querySelectorAll<HTMLElement>(`[${attribute}]`)].find(enabled))
    initial?.focus()
    return initial?.getAttribute(attribute)
  }, attribute)
  expect(first, 'The page has an initial controller focus').toBeTruthy()
  const reached = new Set([first!]),
    queue = [first!]
  const edges: { origin: string; direction: number; next: string }[] = []
  while (queue.length && expected.some((control) => !reached.has(control.id))) {
    expect(reached.size, 'The controller route graph remains bounded').toBeLessThan(250)
    const origin = queue.shift()!
    for (const direction of [12, 13, 14, 15]) {
      const locator = page.locator(`[${attribute}="${origin}"]`)
      // Sort/filter adjustments can unmount virtualized cards outside the surface under test.
      // Every original required button remains mandatory even if its node disappears.
      if (!expected.some((control) => control.id === origin) && !(await locator.count())) break
      await expect(locator).toBeEnabled()
      await locator.evaluate((node) => {
        ;(node as HTMLElement).focus()
        node.scrollIntoView({ block: 'nearest', inline: 'nearest' })
      })
      await tap(direction)
      // Settings may briefly disable the adjusted row while its shared preference is saved.
      await expect
        .poll(() =>
          page.evaluate(
            ({ attribute, ids }) =>
              ids.filter((id) =>
                document
                  .querySelector(`[${attribute}="${id}"]`)
                  ?.matches(':disabled, [aria-disabled="true"]'),
              ),
            { attribute, ids: expected.map((control) => control.id) },
          ),
        )
        .toEqual([])
      await expect
        .poll(async () => {
          await identify(page.locator('body'))
          return page.evaluate((attribute) => {
            const active = document.activeElement
            return active?.hasAttribute(attribute) && !active.matches(':disabled, [aria-disabled="true"]')
              ? active.getAttribute(attribute)
              : null
          }, attribute)
        })
        .toBeTruthy()
        .catch(async (failure) => {
          await test.info().attach('controller-focus-failure', {
            body: JSON.stringify(
              {
                origin,
                direction,
                controls: await identify(page.locator('body')),
                active: await page.evaluate(() => document.activeElement?.outerHTML),
              },
              null,
              2,
            ),
            contentType: 'application/json',
          })
          throw failure
        })
      const next = await page.evaluate(
        (attribute) => document.activeElement!.getAttribute(attribute)!,
        attribute,
      )
      edges.push({ origin, direction, next })
      if (!reached.has(next)) {
        reached.add(next)
        queue.push(next)
      }
    }
  }
  if (expected.some((control) => !reached.has(control.id)))
    await test.info().attach('unreachable-controller-graph', {
      body: JSON.stringify({ expected, current: await identify(page.locator('body')), edges }, null, 2),
      contentType: 'application/json',
    })
  expect(
    expected.filter((control) => !reached.has(control.id)).map((control) => control.label),
    'Every enabled button is reachable through the production D-pad routes',
  ).toEqual([])
  return { controls: expected.length, reached: reached.size }
}

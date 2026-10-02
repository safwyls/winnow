import { readFileSync, readdirSync } from 'node:fs'
import { join, relative, dirname, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import postcss from 'postcss'
import {
  interactiveNameFailures,
  localMotionFailures,
  nameHostFailures,
  schedulingSites,
} from './helpers/renderer-enforcement'
import { cssMotionOwners, scheduledOwners } from './helpers/motion-inventory'

const root = join(import.meta.dirname, '../src/renderer')
function files(directory: string, suffix: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory()
      ? files(join(directory, entry.name), suffix)
      : entry.name.endsWith(suffix)
        ? [join(directory, entry.name)]
        : [],
  )
}
const jsx = files(root, '.tsx')
const sheets = files(root, '.css').map((file) => ({ file, css: postcss.parse(readFileSync(file, 'utf8')) }))
const report = (check: (source: string) => string[]) =>
  jsx.flatMap((file) =>
    check(readFileSync(file, 'utf8')).map((problem) => `${relative(root, file)}:${problem}`),
  )

describe('renderer accessibility and visual discipline enforcement', () => {
  it('every authored intrinsic accessible name belongs to a name-capable semantic host', () => {
    expect(jsx.length).toBeGreaterThan(60)
    expect(report(nameHostFailures)).toEqual([])
  })
  it('every authored intrinsic interactive control has a name or associated label or text', () =>
    expect(report(interactiveNameFailures)).toEqual([]))
  it('rejects generic named hosts, empty controls and local motion instead of accepting their rendered children', () => {
    expect(nameHostFailures('<div aria-label="Lost name"><button>Visible child</button></div>')).toHaveLength(
      1,
    )
    expect(nameHostFailures('<div role="presentation" aria-label="Still lost"/>')).toHaveLength(1)
    expect(nameHostFailures('<div role="invented" aria-label="Unknown"/>')).toHaveLength(1)
    expect(
      nameHostFailures('<div role={shown ? "group" : "invented"} aria-label="Unknown branch"/>'),
    ).toHaveLength(1)
    expect(nameHostFailures('<Unknown aria-label="Lost in an unknown wrapper"/>')).toHaveLength(1)
    expect(
      nameHostFailures('<div role="group" aria-label="Retained group"><button>Child</button></div>'),
    ).toEqual([])
    expect(nameHostFailures('<time aria-label="Local time: 12:00 PM">12:00</time>')).toEqual([])
    expect(interactiveNameFailures('<button><svg/></button>')).toHaveLength(1)
    expect(interactiveNameFailures('<label>Filter<input /></label>')).toEqual([])
    expect(interactiveNameFailures('<input aria-label=""/>')).toHaveLength(1)
    for (const value of ["''", 'null', 'false', 'undefined'])
      expect(interactiveNameFailures(`<input aria-label={${value}}/>`)).toHaveLength(1)
    expect(interactiveNameFailures('<div tabIndex={0}/>')).toHaveLength(1)
    expect(interactiveNameFailures('<div tabIndex={-1}/>')).toHaveLength(1)
    expect(interactiveNameFailures('<div tabIndex={ready ? 0 : -1}/>')).toHaveLength(1)
    for (const value of ['null', 'false', "''"])
      expect(interactiveNameFailures(`<button>{${value}}</button>`)).toHaveLength(1)
    expect(interactiveNameFailures('<label><input/></label>')).toHaveLength(1)
    expect(interactiveNameFailures('<label htmlFor={field}>Filter</label>; <input id={field}/>')).toEqual([])
    expect(localMotionFailures('<button style={{transition:"opacity 2s"}}>Move</button>')).toHaveLength(1)
  })
  it('keeps transition and animation declarations in shared styles rather than JSX local values', () =>
    expect(report(localMotionFailures)).toEqual([]))
  it('inventories every CSS motion owner and every JavaScript scheduling site', () => {
    const motionSheets = sheets
      .filter(({ css }) => {
        let found = false
        css.walkDecls((declaration) => {
          if (/^(animation|transition)(-|$)/.test(declaration.prop)) found = true
        })
        return found
      })
      .map(({ file }) => relative(root, file).replaceAll('\\', '/'))
      .sort()
    expect(motionSheets).toEqual(cssMotionOwners)
    const actual = Object.fromEntries(
      [...files(root, '.ts'), ...jsx].flatMap((file) => {
        const sites = schedulingSites(readFileSync(file, 'utf8'))
        return sites.length ? [[relative(root, file).replaceAll('\\', '/'), sites.length]] : []
      }),
    )
    expect(actual).toEqual(
      Object.fromEntries(Object.entries(scheduledOwners).map(([file, [count]]) => [file, count])),
    )
    for (const [, reason] of Object.values(scheduledOwners)) expect(reason.trim()).not.toBe('')
    expect(
      schedulingSites(
        'const id = requestAnimationFrame(draw); node.animate([], {}); <motion.div animate={{opacity:1}}/>',
      ),
    ).toEqual(['animate', 'motion.div', 'requestAnimationFrame'])
    expect(schedulingSites('window.setTimeout(run, 10); window.setInterval(run, 20);')).toHaveLength(2)
    expect(schedulingSites('type Timer = ReturnType<typeof setTimeout>;')).toEqual([])
  })
  it('suppresses all shared CSS motion for application and OS reduction including pseudo-elements', () => {
    const css = sheets.find(({ file }) => file === join(root, 'styles.css'))!.css
    function complete(source: postcss.Root) {
      let application = false,
        os = false
      source.walkRules((rule) => {
        const declarations = new Map(
          rule.nodes.filter((node) => node.type === 'decl').map((node) => [node.prop, node]),
        )
        const disabled =
          ['animation', 'transition'].every(
            (prop) => declarations.get(prop)?.value === 'none' && declarations.get(prop)?.important,
          ) &&
          declarations.get('scroll-behavior')?.value === 'auto' &&
          declarations.get('scroll-behavior')?.important
        if (!disabled) return
        if (
          ['.reduced-motion *', '.reduced-motion *:before', '.reduced-motion *:after'].every((selector) =>
            rule.selectors.includes(selector),
          )
        )
          application = true
        if (
          rule.parent?.type === 'atrule' &&
          rule.parent.params === '(prefers-reduced-motion: reduce)' &&
          ['*', '*:before', '*:after'].every((selector) => rule.selectors.includes(selector))
        )
          os = true
      })
      return Boolean(application && os)
    }
    expect(complete(css)).toBe(true)
    const broken = css.clone()
    broken.walkDecls('transition', (declaration) => {
      if (declaration.value === 'none') declaration.remove()
    })
    expect(complete(broken)).toBe(false)
  })
  it('all data-face CSS roles inherit tabular figures from the closed explicit role group', () => {
    const data = postcss.parse(readFileSync(join(root, 'components/data-typography.css'), 'utf8'))
    const covered = new Set<string>()
    data.walkRules((rule) => {
      expect(
        rule.nodes.some(
          (node) =>
            node.type === 'decl' &&
            node.prop === 'font-variant-numeric' &&
            node.value === 'tabular-nums' &&
            node.important,
        ),
      ).toBe(true)
      rule.selectors.forEach((selector) => covered.add(selector.trim()))
    })
    const missing: string[] = []
    for (const { file, css } of sheets)
      css.walkRules((rule) => {
        if (
          rule.nodes.some(
            (node) =>
              node.type === 'decl' &&
              ['font', 'font-family'].includes(node.prop) &&
              node.value.includes('var(--font-mono)'),
          )
        )
          for (const selector of rule.selectors)
            if (!covered.has(selector.trim())) missing.push(`${relative(root, file)}: ${selector}`)
      })
    expect(missing).toEqual([])
  })
  it('reserves Flare consumption for unread markers and the Patched bucket', () => {
    const allowed = new Set([
      ".activity-timeline-update[data-unread='true']",
      '.avalon-details-unread',
      '.update-unread-dot',
      '.merge-unread',
      '.avalon-patch-pip',
      '.avalon-unread',
    ])
    const violations: string[] = []
    for (const { file, css } of sheets)
      css.walkDecls((declaration) => {
        if (declaration.prop.startsWith('--') || !/var\(--(?:avalon-)?flare\b/.test(declaration.value)) return
        const parent = declaration.parent
        if (parent?.type !== 'rule' || parent.selectors.some((selector) => !allowed.has(selector.trim())))
          violations.push(`${relative(root, file)}: ${parent?.toString()}`)
        expect(declaration.value).not.toContain('var(--flare,')
      })
    expect(violations).toEqual([])
  })
  it('resolves all three original font roles to bundled TrueType resources', () => {
    const file = join(root, 'themes/avalon.css')
    const faces = new Map<string, string[]>()
    postcss.parse(readFileSync(file, 'utf8')).walkAtRules('font-face', (rule) => {
      const values = new Map(
        (rule.nodes ?? []).filter((node) => node.type === 'decl').map((node) => [node.prop, node.value]),
      )
      const family = values.get('font-family')?.replaceAll("'", '') ?? ''
      const source = /url\(['"]([^'"]+)['"]\)/.exec(values.get('src') ?? '')?.[1]
      if (!source) return
      expect(source.startsWith('./avalon/assets/')).toBe(true)
      const bytes = readFileSync(resolve(dirname(file), source))
      expect(bytes.readUInt32BE(0)).toBe(0x00010000)
      faces.set(family, [...(faces.get(family) ?? []), source])
    })
    expect([...faces.keys()].sort()).toEqual(['Avalon Body', 'Avalon Data', 'Avalon Display'])
    expect(faces.get('Avalon Display')![0]).toContain('BricolageGrotesque')
    expect(faces.get('Avalon Body')![0]).toContain('PlusJakartaSans')
    expect(faces.get('Avalon Data')![0]).toContain('IBMPlexMono')
  })
})

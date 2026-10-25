import { execFileSync, spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const root = resolve(import.meta.dirname, '../../..')
const read = (file: string) => readFileSync(resolve(root, file), 'utf8')
// Scan authored tracked and new files, including dot directories. Build outputs,
// installed dependencies and disposable captures are not repository source.
const files = [
  ...new Set(
    execFileSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], {
      cwd: root,
      encoding: 'utf8',
      windowsHide: true,
    })
      .split('\0')
      .filter(Boolean),
  ),
]
const normalize = (text: string) => text.replace(/\s+/g, ' ')
const deliberate = [
  ['design-system.md', 'a library about your own hoard'],
  ['design-system.md', 'what a\nhoard of them looks like'],
  ['design-system.md', 'look like the whole hoard'],
  ['src/Winnow.App/Views/ActionBarView.axaml', 'look like the whole hoard'],
] as const
const allowedFiles = new Set([
  'AGENTS.md',
  'docs/decisions.md',
  'design-system.md',
  'src/Winnow.App/Views/ActionBarView.axaml',
  'src/Winnow.Application/Services/WinnowDataLocation.cs',
  'src/Winnow.Data/DatabaseInitializer.cs',
  'src/Winnow.Data/SqliteDatabaseCheck.cs',
])
const allowedFragments = [
  'Hoard.Data',
  'hoard.db',
  'LOCALAPPDATA%\\Hoard',
  'Hoard folder',
  'Hoard build',
  'LegacyDefaultId',
  'appearance.theme = hoard',
  '"hoard"',
  'renamed from Hoard',
  'called Hoard',
  'Hoard to Winnow',
  'Upgrading from Hoard',
]
const omittedDocs = new Set([
  'docs/code-review-2026-08-28.md',
  'docs/code-review-2026-09-03.md',
  'docs/stabilization-2026-08-28.md',
])
function strayNames(file: string, text: string) {
  if (allowedFiles.has(file)) return []
  return [...text.matchAll(/\bhoard\b/gi)]
    .filter((match) => {
      const start = Math.max(0, match.index - 40)
      const nearby = text.slice(start, start + 80).toLowerCase()
      // TypeScript also uses single-quoted legacy IDs; only the exact literal is permitted.
      return ![...allowedFragments, "'hoard'"].some((value) => nearby.includes(value.toLowerCase()))
    })
    .map((match) => `${file}:${text.slice(0, match.index).split('\n').length}`)
}
const fakeIds = new Set(['76561197972611406', '76561197971376839', '76561197960265728'])
const foreignIds = (text: string) =>
  [...text.matchAll(/(?<!\d)7656119\d{10}(?!\d)/g)].map((match) => match[0]).filter((id) => !fakeIds.has(id))

describe('repository hygiene carried into the Electron gate', () => {
  it('keeps exactly the single compiling original token source', () => {
    expect(files.filter((file) => /(?:^|\/)tokens\.axaml$/.test(file))).toEqual([
      'src/Winnow.App/Themes/tokens.axaml',
    ])
    expect(read('src/Winnow.App/App.axaml')).toContain('/Themes/tokens.axaml')
  })

  it('preserves all four deliberate common-noun fragments', () => {
    for (const [file, fragment] of deliberate)
      expect(normalize(read(file)), file).toContain(normalize(fragment))
  })

  it('rejects the old product name outside the original noun and compatibility exceptions', () => {
    const scan = files.filter(
      (file) =>
        (file.startsWith('src/') &&
          /\.(?:cs|axaml|tsx?|css|html)$/.test(file) &&
          !file.startsWith('src/Winnow.Electron/tests/')) ||
        (file.endsWith('.md') &&
          !/^(?:docs\/plans|backlog|docs\/spikes)\//.test(file) &&
          !omittedDocs.has(file)),
    )
    expect(scan).toContain('src/Winnow.Electron/src/renderer/App.tsx')
    expect(scan.flatMap((file) => strayNames(file, read(file)))).toEqual([])
    expect(strayNames('src/example.ts', 'Hoard is the product')).toEqual(['src/example.ts:1'])
    expect(strayNames('src/example.ts', "const legacy = 'hoard'")).toEqual([])
  })

  it.each([
    ['Nullable', 'enable'],
    ['ImplicitUsings', 'enable'],
    ['TreatWarningsAsErrors', 'true'],
  ])('retains shared .NET build property %s=%s', (property, value) => {
    expect(read('Directory.Build.props')).toMatch(
      new RegExp(`<${property}>\\s*${value}\\s*</${property}>`, 'i'),
    )
  })

  it('keeps strict TypeScript checking in the production build', () => {
    expect(JSON.parse(read('src/Winnow.Electron/tsconfig.json')).compilerOptions.strict).toBe(true)
    expect(JSON.parse(read('src/Winnow.Electron/package.json')).scripts.build).toMatch(/^tsc --noEmit && /)
  })

  it('retains the original Claude guard and executes the configured Codex guard without writing files', () => {
    const claude = read('.claude/settings.json')
    for (const required of ['PreToolUse', '/backlog/', 'permissionDecision', 'deny', 'backlog CLI'])
      expect(claude).toContain(required)
    const hooks = JSON.parse(read('.codex/hooks.json')).hooks.PreToolUse
    const hook = hooks.find((entry: { matcher: string }) => entry.matcher === '^apply_patch$')
    expect(hook.hooks[0].command).toContain('.codex/hooks/backlog-guard.cjs')
    expect(hook.hooks[0].commandWindows).toContain('.codex/hooks/backlog-guard.cjs')
    for (const operation of ['Add File', 'Update File', 'Delete File', 'Move to']) {
      for (const file of [
        'backlog/tasks/task-999.md',
        'backlog/drafts/task-999.md',
        'backlog/docs/doc-999.md',
        'backlog/decisions/decision-999.md',
        'backlog/milestones/milestone-999.md',
      ]) {
        const result = spawnSync(process.execPath, [resolve(root, '.codex/hooks/backlog-guard.cjs')], {
          cwd: root,
          encoding: 'utf8',
          windowsHide: true,
          input: JSON.stringify({ cwd: root, tool_input: { command: `*** ${operation}: ${file}\n` } }),
        })
        expect(result.status).toBe(0)
        expect(JSON.parse(result.stdout).hookSpecificOutput).toMatchObject({
          hookEventName: 'PreToolUse',
          permissionDecision: 'deny',
        })
      }
    }
    const allowed = spawnSync(process.execPath, [resolve(root, '.codex/hooks/backlog-guard.cjs')], {
      cwd: root,
      encoding: 'utf8',
      windowsHide: true,
      input: JSON.stringify({ cwd: root, tool_input: { command: '*** Update File: README.md\n' } }),
    })
    expect(allowed.status).toBe(0)
    expect(allowed.stdout).toBe('')
  })

  it('allows only the three declared fake Steam IDs in original and Electron fixtures', () => {
    const fixtures = files.filter(
      (file) => file.startsWith('tests/fixtures/') || file.startsWith('src/Winnow.Electron/tests/fixtures/'),
    )
    expect(fixtures.length).toBeGreaterThan(0)
    expect(fixtures.flatMap((file) => foreignIds(read(file)).map((id) => `${file}: ${id}`))).toEqual([])
    expect(foreignIds('76561199999999999')).toEqual(['76561199999999999'])
    expect(foreignIds([...fakeIds].join(' '))).toEqual([])
    expect(foreignIds('1765611999999999990')).toEqual([])
  })
})

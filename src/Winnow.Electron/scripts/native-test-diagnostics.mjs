import { lstat, mkdir, readFile, readdir, writeFile } from 'node:fs/promises'
import { basename, join, resolve } from 'node:path'

const redacted = '[REDACTED]'
const reports = ['full-inventory.json', 'shard-inventory.json', 'results.json']
const pngSignature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])
const credentialName = (name) =>
  /(?:authorization|cookie|password|secret|token|apikey|npsso|clientid|authorizationcode|exchangecode|expectedstate)$/i.test(
    name.replace(/[^a-z0-9]/gi, ''),
  )
const secretValue = (key, value) =>
  (credentialName(key) ||
    (key === 'code' && typeof value === 'string' && !/^(ERR_|E[A-Z_]+$)/.test(value))) &&
  value !== null &&
  typeof value !== 'boolean' &&
  typeof value !== 'number'

function collectCredentials(value, secrets) {
  if (!value || typeof value !== 'object') return
  for (const [key, child] of Object.entries(value)) {
    const namedValue = key === 'value' && credentialName(String(value.key ?? value.name ?? value.id ?? ''))
    if ((secretValue(key, child) || namedValue) && typeof child === 'string' && child.length >= 4)
      secrets.add(child)
    collectCredentials(child, secrets)
  }
}

function cleanText(text, secrets) {
  for (const secret of [...secrets].sort((a, b) => b.length - a.length))
    text = text.replaceAll(secret, redacted)
  return text
    .replace(/\b(Bearer|Basic)\s+[a-z0-9+/_.~=-]+/gi, `$1 ${redacted}`)
    .replace(/\b(Authorization|Cookie|Set-Cookie)\s*:\s*[^\r\n]+/gi, `$1: ${redacted}`)
    .replace(
      /([?&#](?:access_token|refresh_token|token|code|state|client_id|client_secret|api_key|key|npsso)=)[^&#\s"']*/gi,
      `$1${redacted}`,
    )
    .replace(
      /(?<![\w.-])((?:["']?)(?:[\w.-]*(?:client[_-]?secret|password|access[_-]?token|refresh[_-]?token|api[_-]?key|npsso|expectedState)|token)["']?\s*[:=]\s*)(?:"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|[^\s,;}\]]+)/gi,
      `$1"${redacted}"`,
    )
}

// This is a diagnostic boundary for isolated, fake-account tests, not a scanner
// for arbitrary user data. Screenshots retain the authored fixture pixels.
export function sanitizeDiagnostic(value, secrets = new Set()) {
  collectCredentials(value, secrets)
  const visit = (item) => {
    if (typeof item === 'string') return cleanText(item, secrets)
    if (Array.isArray(item)) return item.map(visit)
    if (!item || typeof item !== 'object') return item
    return Object.fromEntries(
      Object.entries(item)
        .filter(([key]) => key !== 'gitDiff')
        .map(([key, child]) => [
          key,
          secretValue(key, child) ||
          (key === 'value' && credentialName(String(item.key ?? item.name ?? item.id ?? '')))
            ? redacted
            : key === 'body' && item.contentType === 'image/png'
              ? child
              : visit(child),
        ]),
    )
  }
  return visit(value)
}

function inlineAttachment(attachment) {
  if (typeof attachment.body !== 'string') return undefined
  const bytes = Buffer.from(attachment.body, 'base64')
  if (attachment.contentType === 'application/json') {
    try {
      return { kind: 'json', value: JSON.parse(bytes.toString('utf8')) }
    } catch {
      return undefined
    }
  }
  if (attachment.contentType === 'image/png' && bytes.subarray(0, 8).equals(pngSignature))
    return { kind: 'png', value: attachment.body }
  return undefined
}

function visitResults(report, action) {
  for (const spec of report.specs ?? [])
    for (const test of spec.tests ?? []) for (const result of test.results ?? []) action(result)
  for (const suite of report.suites ?? []) visitResults(suite, action)
}

async function regularFile(path) {
  try {
    const stat = await lstat(path)
    return stat.isFile() && !stat.isSymbolicLink() && stat.nlink === 1
  } catch (error) {
    if (error.code === 'ENOENT') return false
    throw error
  }
}

export async function retainNativeDiagnostics(rawDirectory, retainedDirectory) {
  const inputs = new Map(),
    secrets = new Set(),
    stems = new Set()
  const summary = { schema: 1, reports: [], artifacts: [], excludedFiles: 0, invalidFiles: [] }
  for (const name of reports) {
    const source = join(rawDirectory, name)
    if (!(await regularFile(source))) continue
    try {
      const report = JSON.parse(await readFile(source, 'utf8'))
      inputs.set(name, report)
      const collect = (suite) => {
        for (const spec of suite.specs ?? [])
          stems.add(basename(spec.file.replaceAll('\\', '/')).replace(/\.spec\.[cm]?[jt]sx?$/, ''))
        for (const child of suite.suites ?? []) collect(child)
      }
      collect(report)
      collectCredentials(report, secrets)
      visitResults(report, (result) => {
        for (const attachment of result.attachments ?? []) {
          const decoded = inlineAttachment(attachment)
          if (decoded?.kind === 'json') collectCredentials(decoded.value, secrets)
        }
      })
    } catch {
      summary.invalidFiles.push(name)
    }
  }

  // Only immediate test outputs and Playwright's attachment subdirectory qualify.
  // Never descend into Chromium profiles, fixture data, archives or linked trees.
  const artifacts = [],
    rawArtifacts = join(rawDirectory, 'artifacts')
  const list = async (directory) => {
    try {
      const stat = await lstat(directory)
      return stat.isDirectory() && !stat.isSymbolicLink() ? await readdir(directory) : []
    } catch (error) {
      if (error.code === 'ENOENT') return []
      throw error
    }
  }
  for (const directory of await list(rawArtifacts)) {
    if (![...stems].some((stem) => directory === stem || directory.startsWith(`${stem}-`))) {
      summary.excludedFiles++
      continue
    }
    for (const name of await list(join(rawArtifacts, directory))) {
      const names =
        name === 'attachments'
          ? (await list(join(rawArtifacts, directory, name))).map((file) => join(name, file))
          : [name]
      for (const file of names) {
        const relative = join(directory, file),
          source = join(rawArtifacts, relative)
        const kind = /\.png$/i.test(file)
          ? 'png'
          : /\.json$/i.test(file)
            ? 'json'
            : file === 'error-context.md'
              ? 'context'
              : undefined
        if (!kind || !(await regularFile(source))) {
          summary.excludedFiles++
          continue
        }
        try {
          const bytes = await readFile(source)
          const value =
            kind === 'json'
              ? JSON.parse(bytes.toString('utf8'))
              : kind === 'context'
                ? bytes.toString('utf8')
                : bytes
          if (kind === 'png' && !bytes.subarray(0, 8).equals(pngSignature)) throw Error('Invalid PNG')
          if (kind === 'json') collectCredentials(value, secrets)
          artifacts.push({ source: resolve(source), relative, kind, value })
        } catch {
          summary.invalidFiles.push(join('artifacts', relative).replaceAll('\\', '/'))
        }
      }
    }
  }
  const retainedPaths = new Map()
  for (const artifact of artifacts) {
    const name = join('artifacts', artifact.relative).replaceAll('\\', '/')
    const destination = join(retainedDirectory, name)
    await mkdir(join(destination, '..'), { recursive: true })
    const body =
      artifact.kind === 'png'
        ? artifact.value
        : artifact.kind === 'json'
          ? JSON.stringify(sanitizeDiagnostic(artifact.value, secrets), null, 2) + '\n'
          : cleanText(artifact.value, secrets)
    await writeFile(destination, body)
    retainedPaths.set(artifact.source, name)
    summary.artifacts.push(name)
  }
  for (const [name, report] of inputs) {
    visitResults(report, (result) => {
      result.attachments = (result.attachments ?? []).flatMap((attachment) => {
        const path =
          typeof attachment.path === 'string' ? retainedPaths.get(resolve(attachment.path)) : undefined
        const decoded = inlineAttachment(attachment)
        if (!path && !decoded) return []
        return [
          {
            name: cleanText(attachment.name ?? '', secrets),
            contentType: attachment.contentType,
            ...(path ? { path } : {}),
            ...(decoded
              ? {
                  body:
                    decoded.kind === 'png'
                      ? decoded.value
                      : Buffer.from(JSON.stringify(sanitizeDiagnostic(decoded.value, secrets))).toString(
                          'base64',
                        ),
                }
              : {}),
          },
        ]
      })
      for (const channel of ['stdout', 'stderr'])
        if (result[channel])
          result[channel] = result[channel].map((entry) =>
            typeof entry.buffer === 'string'
              ? { text: cleanText(Buffer.from(entry.buffer, 'base64').toString('utf8'), secrets) }
              : sanitizeDiagnostic(entry, secrets),
          )
    })
    await writeFile(
      join(retainedDirectory, name),
      JSON.stringify(sanitizeDiagnostic(report, secrets), null, 2) + '\n',
    )
    summary.reports.push(name)
  }
  await writeFile(join(retainedDirectory, 'retention.json'), JSON.stringify(summary, null, 2) + '\n')
  return summary
}

export async function withRetainedDiagnostics(rawDirectory, retainedDirectory, run) {
  try {
    return await run()
  } finally {
    await retainNativeDiagnostics(rawDirectory, retainedDirectory)
  }
}

import { randomUUID, createHash } from 'node:crypto'
import { createReadStream } from 'node:fs'
import { mkdir, rm } from 'node:fs/promises'
import { join } from 'node:path'
import type { UpdateDriver } from './application-updater'
import {
  distributionRelease,
  downloadDistribution,
  type DistributionKind,
  type DistributionRelease,
} from './distribution-release'
import { distributionHelper, runDistributionHelper } from './distribution-helper'

interface Options {
  version: string
  runtime: 'win-x64' | 'linux-x64'
  kind: DistributionKind
  supported: boolean
  installation: string
  executable: string
  dataDirectory: string
  args: readonly string[]
  quit(): void
  check?: typeof distributionRelease
  download?: typeof downloadDistribution
  helper?: typeof runDistributionHelper
}

async function verifyArchive(path: string, release: DistributionRelease) {
  let size = 0
  const hash = createHash('sha256')
  for await (const chunk of createReadStream(path)) {
    size += chunk.length
    if (size > release.size) throw Error('The staged update size changed.')
    hash.update(chunk)
  }
  if (size !== release.size || hash.digest('hex') !== release.sha256)
    throw Error('The staged update checksum changed.')
}

/** Primary releases keep the existing installer identity and durable portable recovery engine. */
export function distributionUpdateDriver(options: Options): UpdateDriver {
  const helper = distributionHelper(options.installation)
  const run = options.helper ?? runDistributionHelper
  let release: DistributionRelease | null = null
  let channel: boolean | undefined
  let directory: string | undefined,
    archive: string | undefined,
    journal: string | undefined,
    transaction: string | undefined
  const discard = async () => {
    if (journal && transaction)
      await run(helper, ['discard', '--journal', journal, '--transaction', transaction])
    journal = undefined
    transaction = undefined
    archive = undefined
    if (directory) await rm(directory, { recursive: true, force: true })
    directory = undefined
  }
  return {
    supported: options.supported,
    async check(includeBeta, signal) {
      const selected = await (options.check ?? distributionRelease)({
        version: options.version,
        includeBeta,
        runtime: options.runtime,
        kind: options.kind,
        signal,
      })
      const sameVersion = selected && selected.version === release?.version
      const sameArtifact =
        sameVersion &&
        selected.sha256 === release?.sha256 &&
        selected.size === release?.size &&
        selected.assetUrl === release?.assetUrl
      if (archive && sameVersion && !sameArtifact) {
        await discard()
        release = null
        throw Error('The staged update metadata changed. Check again before downloading.')
      }
      if (!sameArtifact || includeBeta !== channel) await discard()
      else if (archive && selected) {
        try {
          await verifyArchive(archive, selected)
        } catch (error) {
          await discard()
          release = null
          throw error
        }
      }
      release = selected
      channel = includeBeta
      return release
    },
    discard,
    async download(selected, signal, progress) {
      if (
        !options.supported ||
        !release ||
        selected.version !== release.version ||
        selected.downloadUrl !== release.downloadUrl ||
        selected.releaseUrl !== release.releaseUrl
      )
        throw Error('Check for a supported update before downloading.')
      await discard()
      signal.throwIfAborted()
      directory = join(options.dataDirectory, 'updates', `electron-${randomUUID()}`)
      await mkdir(directory, { recursive: true })
      archive = join(
        directory,
        options.kind === 'installed'
          ? 'setup.exe'
          : options.runtime === 'win-x64'
            ? 'update.zip'
            : 'update.tar.gz',
      )
      try {
        await (options.download ?? downloadDistribution)(release, archive, signal, progress)
        await verifyArchive(archive, release)
        signal.throwIfAborted()
        if (options.kind === 'portable') {
          transaction = randomUUID().replaceAll('-', '')
          // Do not kill staging halfway through its durable writes: wait, then discard on cancellation.
          journal = await run(helper, [
            'stage',
            '--archive',
            archive,
            '--sha256',
            release.sha256,
            '--version',
            release.version,
            '--runtime',
            options.runtime,
            '--installation',
            options.installation,
            '--data-dir',
            options.dataDirectory,
            '--executable',
            options.executable,
            '--transaction',
            transaction,
            ...(options.args.includes('--no-sync') ? ['--no-sync'] : []),
          ])
          signal.throwIfAborted()
        }
      } catch (error) {
        await discard()
        throw error
      }
    },
    async install() {
      if (!options.supported || !release || !archive) throw Error('Download and verify the update first.')
      try {
        await verifyArchive(archive, release)
        if (options.kind === 'portable' && journal && transaction) {
          await run(helper, [
            'handoff',
            '--journal',
            journal,
            '--transaction',
            transaction,
            '--pid',
            String(process.pid),
          ])
        } else if (options.kind === 'installed') {
          await run(helper, [
            'installed-handoff',
            '--installer',
            archive,
            '--sha256',
            release.sha256,
            '--installation',
            options.installation,
            '--executable',
            join(options.installation, options.executable),
            '--data-dir',
            options.dataDirectory,
            '--pid',
            String(process.pid),
            ...(options.args.includes('--no-sync') ? ['--no-sync'] : []),
          ])
        } else throw Error('This installation requires its package manager.')
      } catch (error) {
        await discard()
        throw error
      }
      // The helper owns these files after the proceed handshake; disposal must not discard them.
      journal = undefined
      transaction = undefined
      archive = undefined
      directory = undefined
      options.quit()
    },
  }
}

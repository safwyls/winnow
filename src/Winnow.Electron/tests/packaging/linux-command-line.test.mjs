import assert from 'node:assert/strict'
import { test } from 'node:test'
import { inspectLinuxCommandLine } from '../packaged/linux-command-line.mjs'

test('real argv preserves paths with spaces and only reads complete switch arguments', () => {
  const command = '/tmp/Winnow package/Winnow'
  const result = inspectLinuxCommandLine(
    [command, '--type=renderer', '--user-data-dir=/tmp/profile --no-sandbox', '--enable-sandbox', ''].join(
      '\0',
    ),
  )
  assert.deepEqual(result, {
    command,
    representation: 'argv',
    type: 'renderer',
    disabledSandboxArguments: [],
  })
})

test('Chromium rewritten renderer title retains the whole title and recognizes bounded switches', () => {
  const command =
    '/home/runner/artifacts/Winnow package/Winnow --type=renderer --enable-crash-reporter=fixture,no_channel ' +
    '--user-data-dir=/home/runner/.tmp/winnow primary packaged fixture/electron-userdata --enable-sandbox ' +
    '--app-path=/home/runner/artifacts/Winnow package/resources/app.asar --remote-debugging-port=0 ' +
    '--ozone-platform=x11 --renderer-client-id=4 --shared-files=v8_context_snapshot_data:100'
  assert.deepEqual(inspectLinuxCommandLine(`${command}\0\0`), {
    command,
    representation: 'chromium-title',
    type: 'renderer',
    disabledSandboxArguments: [],
  })
})

for (const flag of [
  '--no-sandbox',
  '--disable-sandbox',
  '--disable-setuid-sandbox',
  '--disable-seccomp-filter-sandbox',
  '--disable-namespace-sandbox',
  '--no-zygote-sandbox',
])
  for (const suffix of ['', '=true', '=false'])
    test(`rejects ${flag}${suffix} in both argv and rewritten titles`, () => {
      const argument = `${flag}${suffix}`
      for (const command of [
        `/opt/winnow/Winnow\0--type=renderer\0${argument}\0`,
        `/opt/winnow/Winnow --type=renderer ${argument}\0`,
      ]) {
        const result = inspectLinuxCommandLine(command)
        assert.equal(result.type, 'renderer')
        assert.deepEqual(result.disabledSandboxArguments, [argument])
      }
    })

test('does not mistake longer flag names or path fragments for sandbox switches', () => {
  const result = inspectLinuxCommandLine(
    '/tmp/--no-sandbox/Winnow --type=renderer --disable-sandboxing --no-sandbox-extra ' +
      '--user-data-dir=/tmp/--no-sandbox/profile --custom-type=utility --type=renderer/path\0',
  )
  assert.equal(result.type, 'renderer')
  assert.deepEqual(result.disabledSandboxArguments, [])
})

test('records all forbidden switches in order, including whitespace-delimited rewritten titles', () => {
  const result = inspectLinuxCommandLine(
    '/opt/winnow/Winnow\t--type=renderer\n--no-sandbox --disable-sandbox=1\0',
  )
  assert.equal(result.type, 'renderer')
  assert.deepEqual(result.disabledSandboxArguments, ['--no-sandbox', '--disable-sandbox=1'])
})

test('does not invent a process type for a main process or empty cmdline', () => {
  assert.equal(inspectLinuxCommandLine('/opt/winnow/Winnow\0--no-sync\0').type, null)
  assert.equal(
    inspectLinuxCommandLine('/opt/winnow/Winnow --user-data-dir=/tmp/path with spaces\0').type,
    null,
  )
  assert.deepEqual(inspectLinuxCommandLine(''), {
    command: null,
    representation: 'argv',
    type: null,
    disabledSandboxArguments: [],
  })
})

test('records internal zygote flags without applying the renderer assertion to unrelated children', () => {
  assert.deepEqual(inspectLinuxCommandLine('/opt/winnow/Winnow --type=zygote --no-zygote-sandbox\0'), {
    command: '/opt/winnow/Winnow --type=zygote --no-zygote-sandbox',
    representation: 'chromium-title',
    type: 'zygote',
    disabledSandboxArguments: ['--no-zygote-sandbox'],
  })
})

import { EventEmitter } from 'node:events'
import { PassThrough } from 'node:stream'
import { spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { win32 } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { controllerBatteryLabel, validateControllerSample } from '../src/shared/controller-status'
import {
  matchingControllerBattery,
  parseNativeControllers,
  WindowsControllerProbe,
} from '../src/main/controller-battery'
import { controllerBatteryScript } from '../src/main/controller-battery-script'

const sample = () => ({
  id: 'Xbox 360 Controller (XInput STANDARD GAMEPAD)',
  index: 7,
  buttons: Array(16).fill(0),
  axes: [0, 0, 0, 0],
})
const reading = (slot = 2, type = 2, level = 1) => ({
  slot,
  type,
  level,
  buttons: Array(16).fill(0),
  axes: [0, 0, 0, 0],
})
afterEach(() => vi.useRealTimers())

describe('controller battery source contracts', () => {
  it.each([
    [0, 0, null],
    [255, 3, null],
    [1, 0, 'Wired controller'],
    [2, 1, 'Controller battery low'],
    [3, 3, 'Controller battery full'],
  ] as const)('UnknownBatteryIsAbsentRatherThanAnError: type %s level %s', (type, level, label) =>
    expect(controllerBatteryLabel(type, level)).toBe(label),
  )
  it.each([2, 3])('maps all four known battery levels for type %s', (type) => {
    expect([0, 1, 2, 3].map((level) => controllerBatteryLabel(type, level))).toEqual([
      'Controller battery empty',
      'Controller battery low',
      'Controller battery medium',
      'Controller battery full',
    ])
    for (const level of [null, -1, 4, 1.5, NaN]) expect(controllerBatteryLabel(type, level)).toBeNull()
  })
  it('matches the selected live device without equating browser and native indexes', () => {
    const other = reading(0, 3, 3)
    other.buttons[0] = 1
    expect(matchingControllerBattery(sample(), [other, reading()])).toBe('Controller battery low')
    const active = sample()
    active.buttons[0] = 1
    expect(matchingControllerBattery(active, [other, reading()])).toBe('Controller battery full')
    expect(matchingControllerBattery(sample(), [reading(0), reading(1, 3, 3)])).toBeNull()
    expect(matchingControllerBattery(sample(), [other])).toBeNull()
    expect(matchingControllerBattery({ ...sample(), id: 'unsupported device' }, [reading()])).toBeNull()
    expect(matchingControllerBattery(sample(), [])).toBeNull()
  })
  it('validates and copies the bounded IPC sample', () => {
    const input = sample(),
      copy = validateControllerSample(input)
    input.buttons[0] = 1
    expect(copy.buttons[0]).toBe(0)
    for (const value of [
      null,
      {},
      { ...sample(), id: 'a'.repeat(257) },
      { ...sample(), index: -1 },
      { ...sample(), index: 16 },
      { ...sample(), buttons: [0] },
      { ...sample(), axes: [0, NaN, 0, 0] },
      { ...sample(), axes: [0, 2, 0, 0] },
    ])
      expect(() => validateControllerSample(value)).toThrow('Invalid controller sample')
  })
  it('rejects malformed, oversized and duplicate native responses', () => {
    expect(parseNativeControllers('\uFEFF' + JSON.stringify([reading()]))).toEqual([reading()])
    for (const value of [
      {},
      [reading(), reading()],
      Array(5).fill(reading()),
      [{ ...reading(), slot: 4 }],
      [{ ...reading(), buttons: [] }],
      [{ ...reading(), type: '2' }],
      [{ ...reading(), level: 256 }],
    ])
      expect(() => parseNativeControllers(JSON.stringify(value))).toThrow(
        'Invalid native controller response',
      )
  })
})

function childFixture() {
  return Object.assign(new EventEmitter(), {
    stdout: new PassThrough(),
    stderr: new PassThrough(),
    stdin: new PassThrough(),
    kill: vi.fn(),
  }) as unknown as ChildProcessWithoutNullStreams
}
describe('native controller helper lifetime', () => {
  it('coalesces reads, accepts split lines and releases the child and pending request', async () => {
    vi.useFakeTimers()
    const child = childFixture(),
      launch = vi.fn(() => child),
      probe = new WindowsControllerProbe('win32', launch)
    const first = probe.read()
    expect(probe.read()).toBe(first)
    child.stdout.emit('data', JSON.stringify([reading()]))
    child.stdout.emit('data', '\r\n')
    expect(await first).toEqual([reading()])
    const pending = probe.read()
    probe.dispose()
    expect(await pending).toEqual([])
    expect(child.kill).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)
    expect(launch).toHaveBeenCalledOnce()
  })
  it.each(['error', 'exit', 'timeout', 'invalid', 'oversized', 'stdin'] as const)(
    'soft-fails %s and backs off before restarting',
    async (failure) => {
      vi.useFakeTimers()
      const child = childFixture(),
        fresh = childFixture(),
        launch = vi.fn().mockReturnValueOnce(child).mockReturnValue(fresh),
        probe = new WindowsControllerProbe('win32', launch)
      const pending = probe.read()
      if (failure === 'timeout') await vi.advanceTimersByTimeAsync(5000)
      else if (failure === 'invalid') child.stdout.emit('data', 'bad\n')
      else if (failure === 'oversized') child.stdout.emit('data', 'a'.repeat(16385))
      else if (failure === 'stdin') child.stdin.emit('error', Error('closed'))
      else child.emit(failure, failure === 'error' ? Error('missing') : 1)
      expect(await pending).toEqual([])
      expect(await probe.read()).toEqual([])
      expect(launch).toHaveBeenCalledOnce()
      await vi.advanceTimersByTimeAsync(5000)
      const next = probe.read()
      child.emit('exit', 1)
      child.stdout.emit('data', 'bad\n')
      fresh.stdout.emit('data', '[]\n')
      expect(await next).toEqual([])
      expect(launch).toHaveBeenCalledTimes(2)
      probe.dispose()
      expect(vi.getTimerCount()).toBe(0)
    },
  )
  it('does not start a Windows helper on unsupported platforms', async () => {
    const launch = vi.fn()
    expect(await new WindowsControllerProbe('linux', launch).read()).toEqual([])
    expect(launch).not.toHaveBeenCalled()
  })
})

async function runScript(script: string, input: string) {
  const child = spawn(
    win32.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32/WindowsPowerShell/v1.0/powershell.exe'),
    [
      '-NoLogo',
      '-NoProfile',
      '-NonInteractive',
      '-EncodedCommand',
      Buffer.from(script, 'utf16le').toString('base64'),
    ],
    { windowsHide: true },
  )
  let stdout = '',
    stderr = ''
  child.stdout.on('data', (chunk) => (stdout += chunk))
  child.stderr.on('data', (chunk) => (stderr += chunk))
  const timeout = setTimeout(() => child.kill(), 10000)
  try {
    const exit = new Promise<number | null>((resolve, reject) => {
      child.on('error', reject)
      child.on('exit', resolve)
    })
    child.stdin.end(input)
    expect(await exit, stderr).toBe(0)
    expect(stderr).toBe('')
    return stdout.trim().split(/\r?\n/)
  } finally {
    clearTimeout(timeout)
    child.kill()
  }
}
describe.skipIf(process.platform !== 'win32')('executed Windows helper', () => {
  it('compiles and reads real system XInput without requiring connected hardware', async () => {
    const lines = await runScript(controllerBatteryScript, 'read\nread\n')
    expect(lines).toHaveLength(2)
    for (const line of lines) expect(() => parseNativeControllers(line)).not.toThrow()
  }, 15000)
  it('caches battery for 30 seconds, resets on disconnect and normalizes native controls', async () => {
    // Replace only OS/time boundaries; execute the production C# cache and normalization unchanged.
    const fixture = controllerBatteryScript
      .replace(
        /  \[DllImport[\s\S]*?static extern uint GetBattery\(uint index, byte device, out Battery battery\);/,
        `
  public static bool Connected = true; public static int Calls; public static long Now = 100;
  static uint GetState(uint index, out State state) {
    state = new State { Buttons = 4096 | 32 | 8, LeftTrigger = 255, RightTrigger = 128, LeftX = -32768, LeftY = 32767, RightX = 0, RightY = 0 };
    return index == 2 && Connected ? 0u : 1167u;
  }
  static uint GetBattery(uint index, byte device, out Battery battery) {
    Calls++; battery = new Battery { Type = 2, Level = (byte)Calls }; return 0;
  }`,
      )
      .replace('System.Diagnostics.Stopwatch.GetTimestamp()', 'Now')
      .replace('System.Diagnostics.Stopwatch.Frequency * 30', '30')
    const prefix = fixture.slice(0, fixture.indexOf('while ($null'))
    const lines = await runScript(
      prefix +
        `
$first = [WinnowControllerProbe]::Read()
[WinnowControllerProbe]::Now = 129
$cached = [WinnowControllerProbe]::Read()
[WinnowControllerProbe]::Now = 130
$refreshed = [WinnowControllerProbe]::Read()
[WinnowControllerProbe]::Connected = $false
$disconnected = @([WinnowControllerProbe]::Read())
[WinnowControllerProbe]::Connected = $true
$reconnected = [WinnowControllerProbe]::Read()
[Console]::WriteLine((ConvertTo-Json -Compress -Depth 5 @{first=$first; cached=$cached; refreshed=$refreshed; disconnected=$disconnected; reconnected=$reconnected; calls=[WinnowControllerProbe]::Calls}))
`,
      '',
    )
    const result = JSON.parse(lines[0])
    expect(result.calls).toBe(3)
    expect(result.first[0].level).toBe(1)
    expect(result.cached[0].level).toBe(1)
    expect(result.refreshed[0].level).toBe(2)
    expect(result.disconnected).toEqual([])
    expect(result.reconnected[0].level).toBe(3)
    expect(result.first[0].buttons).toEqual([1, 0, 0, 0, 0, 0, 1, 128 / 255, 1, 0, 0, 0, 0, 0, 0, 1])
    expect(result.first[0].axes[0]).toBe(-1)
    expect(result.first[0].axes[1]).toBe(-1)
  }, 15000)
})

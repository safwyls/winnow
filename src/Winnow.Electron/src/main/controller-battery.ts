import { spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { win32 } from 'node:path'
import { controllerBatteryLabel, type ControllerSample } from '../shared/controller-status'
import { controllerBatteryScript } from './controller-battery-script'

export interface NativeControllerReading {
  slot: number
  buttons: number[]
  axes: number[]
  type: number | null
  level: number | null
}

export function matchingControllerBattery(
  sample: ControllerSample,
  readings: NativeControllerReading[],
): string | null {
  if (!sample.id.includes('(XInput STANDARD GAMEPAD)')) return null
  // Chromium's index is a browser allocation, not the Windows XInput slot.
  // Identical live states cannot identify a device, so omit their battery rather than guess.
  const matches = readings.filter(
    (reading) =>
      reading.buttons.every((value, i) => Math.abs(value - sample.buttons[i]) <= 0.002) &&
      reading.axes.every((value, i) => Math.abs(value - sample.axes[i]) <= 0.002),
  )
  return matches.length === 1 ? controllerBatteryLabel(matches[0].type, matches[0].level) : null
}

export function parseNativeControllers(text: string): NativeControllerReading[] {
  const rows: unknown = JSON.parse(text.replace(/^\uFEFF/, ''))
  if (!Array.isArray(rows) || rows.length > 4) throw Error('Invalid native controller response')
  const slots = new Set<number>()
  for (const row of rows) {
    const vector = (values: unknown, size: number, minimum: number) =>
      Array.isArray(values) &&
      values.length === size &&
      values.every(
        (value) => typeof value === 'number' && Number.isFinite(value) && value >= minimum && value <= 1,
      )
    const byte = (value: unknown) =>
      value === null || (Number.isInteger(value) && Number(value) >= 0 && Number(value) <= 255)
    if (
      !row ||
      !Number.isInteger(row.slot) ||
      row.slot < 0 ||
      row.slot > 3 ||
      slots.has(row.slot) ||
      !vector(row.buttons, 16, 0) ||
      !vector(row.axes, 4, -1) ||
      !byte(row.type) ||
      !byte(row.level)
    )
      throw Error('Invalid native controller response')
    slots.add(row.slot)
  }
  return rows as NativeControllerReading[]
}

export class WindowsControllerProbe {
  private child: ChildProcessWithoutNullStreams | null = null
  private pending: {
    promise: Promise<NativeControllerReading[]>
    resolve(value: NativeControllerReading[]): void
    timer: ReturnType<typeof setTimeout>
  } | null = null
  private buffer = ''
  private retryAt = 0
  constructor(
    private readonly platform: string = process.platform,
    private readonly launch = () =>
      spawn(
        win32.join(
          process.env.SystemRoot ?? 'C:\\Windows',
          'System32',
          'WindowsPowerShell',
          'v1.0',
          'powershell.exe',
        ),
        [
          '-NoLogo',
          '-NoProfile',
          '-NonInteractive',
          '-EncodedCommand',
          Buffer.from(controllerBatteryScript, 'utf16le').toString('base64'),
        ],
        { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] },
      ),
  ) {}

  read(): Promise<NativeControllerReading[]> {
    if (this.platform !== 'win32' || Date.now() < this.retryAt) return Promise.resolve([])
    if (this.pending) return this.pending.promise
    try {
      if (!this.child) {
        const child = (this.child = this.launch())
        this.buffer = ''
        child.stdout.setEncoding('utf8')
        child.stdout.on('data', (chunk: string) => {
          if (this.child !== child) return
          this.buffer += chunk
          if (this.buffer.length > 16384) {
            this.fail()
            return
          }
          const newline = this.buffer.indexOf('\n')
          if (newline < 0) return
          const line = this.buffer.slice(0, newline)
          this.buffer = this.buffer.slice(newline + 1)
          try {
            this.finish(parseNativeControllers(line))
          } catch {
            this.fail()
          }
        })
        child.stderr.resume()
        child.on('error', () => {
          if (this.child === child) this.fail()
        })
        child.on('exit', () => {
          if (this.child === child) this.fail()
        })
        child.stdin.on('error', () => {
          if (this.child === child) this.fail()
        })
      }
      let resolve!: (value: NativeControllerReading[]) => void
      const promise = new Promise<NativeControllerReading[]>((done) => {
        resolve = done
      })
      this.pending = { promise, resolve, timer: setTimeout(() => this.fail(), 5000) }
      this.child.stdin.write('read\n')
      return promise
    } catch {
      this.fail()
      return Promise.resolve([])
    }
  }

  private finish(value: NativeControllerReading[]) {
    const pending = this.pending
    this.pending = null
    if (pending) {
      clearTimeout(pending.timer)
      pending.resolve(value)
    }
  }
  private fail() {
    this.retryAt = Date.now() + 5000
    this.dispose()
  }
  dispose() {
    const child = this.child
    this.child = null
    this.buffer = ''
    this.finish([])
    child?.kill()
  }
}

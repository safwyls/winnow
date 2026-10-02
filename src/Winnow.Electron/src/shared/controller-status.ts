export interface ControllerSample {
  id: string
  index: number
  buttons: number[]
  axes: number[]
}

export function selectedStandardController<T extends { connected: boolean; mapping: string }>(
  pads: readonly (T | null)[],
): T | null {
  return pads.find((pad): pad is T => !!pad?.connected && pad.mapping === 'standard') ?? null
}

export function controllerSample(pad: Gamepad): ControllerSample {
  return {
    id: pad.id ?? '',
    index: pad.index,
    buttons: Array.from(
      { length: 16 },
      (_, i) => pad.buttons[i]?.value ?? Number(pad.buttons[i]?.pressed ?? false),
    ),
    axes: Array.from({ length: 4 }, (_, i) => pad.axes[i] ?? 0),
  }
}

export function validateControllerSample(value: unknown): ControllerSample {
  if (!value || typeof value !== 'object') throw Error('Invalid controller sample')
  const sample = value as ControllerSample
  const vector = (values: unknown, length: number, minimum: number) =>
    Array.isArray(values) &&
    values.length === length &&
    values.every((item) => typeof item === 'number' && Number.isFinite(item) && item >= minimum && item <= 1)
  if (
    typeof sample.id !== 'string' ||
    sample.id.length > 256 ||
    !Number.isInteger(sample.index) ||
    sample.index < 0 ||
    sample.index > 15 ||
    !vector(sample.buttons, 16, 0) ||
    !vector(sample.axes, 4, -1)
  )
    throw Error('Invalid controller sample')
  return { id: sample.id, index: sample.index, buttons: [...sample.buttons], axes: [...sample.axes] }
}

export function controllerBatteryLabel(type: number | null, level: number | null): string | null {
  if (type === 1) return 'Wired controller'
  if ((type === 2 || type === 3) && level !== null && Number.isInteger(level) && level >= 0 && level <= 3)
    return `Controller battery ${['empty', 'low', 'medium', 'full'][level]}`
  return null
}

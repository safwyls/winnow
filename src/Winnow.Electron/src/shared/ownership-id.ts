/** URL and activation IDs retain SQLite's positive signed 64-bit range without rounding. */
export function ownershipId(value: unknown): number | string | null {
  if (typeof value === 'number') return Number.isSafeInteger(value) && value > 0 ? value : null
  if (typeof value !== 'string' || !/^[1-9]\d{0,18}$/.test(value)) return null
  const integer = BigInt(value)
  if (integer > 9223372036854775807n) return null
  return integer <= BigInt(Number.MAX_SAFE_INTEGER) ? Number(value) : value
}

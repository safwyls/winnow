/** The backend counts whole seconds; subtract local calendar days to retain the user's time of day across DST. */
export function journalPeriod(days: number, now = new Date()) {
  if (!Number.isInteger(days) || days <= 0 || !Number.isFinite(now.getTime()))
    throw new RangeError('A journal period needs a valid date and a positive whole number of days.')
  const until = new Date(now)
  until.setUTCMilliseconds(0)
  const from = new Date(until)
  from.setDate(from.getDate() - days)
  return { fromUtc: from.toISOString(), untilUtc: until.toISOString() }
}

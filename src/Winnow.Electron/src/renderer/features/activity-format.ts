const dateFormatter = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
const hoursFormatter = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 })

// Large local histories share the same display policy without allocating a formatter per row.
export const activityDateLabel = (value: string) => {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? 'Invalid Date' : dateFormatter.format(date)
}
export const activityHours = (minutes: number) =>
  minutes < 60 ? `${Math.round(minutes)} min` : `${hoursFormatter.format(minutes / 60)} hr`

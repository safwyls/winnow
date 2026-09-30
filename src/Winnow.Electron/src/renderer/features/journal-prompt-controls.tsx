export function sessionDuration(seconds: number) {
  const minutes = Math.floor(Math.max(0, seconds) / 60)
  if (minutes < 60) return `${minutes}m`
  const hours = Math.floor(minutes / 60),
    rest = minutes % 60
  return rest ? `${hours}h ${rest}m` : `${hours}h`
}

export function JournalRating({
  value,
  disabled,
  fullscreen,
  change,
}: {
  value: number
  disabled: boolean
  fullscreen: boolean
  change(value: number): void
}) {
  return (
    <div className="journal-rating" role="group" aria-label="Your rating">
      <input type="hidden" name="rating" value={value} />
      {[1, 2, 3, 4, 5].map((rating) => (
        <button
          type="button"
          key={rating}
          disabled={disabled}
          aria-label={`${rating} out of 5`}
          title={`${rating} out of 5`}
          aria-pressed={value === rating}
          data-filled={value >= rating}
          onClick={() => change(value === rating ? 0 : rating)}
        >
          {fullscreen ? `${rating} / 5` : <span aria-hidden="true" />}
        </button>
      ))}
      {fullscreen && <p className="journal-current-rating">Rating: {value} / 5</p>}
    </div>
  )
}

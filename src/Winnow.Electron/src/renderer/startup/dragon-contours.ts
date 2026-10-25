/** The shipped mark uses closed contours; relative moves after close are relative to the prior start. */
export function closedDragonContours(paths: string[]) {
  return paths.flatMap((path) => {
    let x = 0,
      y = 0
    return path
      .split(/(?=[Mm])/)
      .filter(Boolean)
      .map((contour) => {
        const move = /^([Mm])\s*(-?\d*\.?\d+(?:e[+-]?\d+)?)[\s,]+(-?\d*\.?\d+(?:e[+-]?\d+)?)/i.exec(contour)
        if (!move || !/[zZ]\s*$/.test(contour)) throw new Error('The loading mark requires closed contours.')
        x = Number(move[2]) + (move[1] === 'm' ? x : 0)
        y = Number(move[3]) + (move[1] === 'm' ? y : 0)
        return `M${x},${y}${contour.slice(move[0].length)}`
      })
  })
}

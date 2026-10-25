import '../../src/renderer/styles.css'
import { useEffect, useState, type CSSProperties } from 'react'
import { createRoot } from 'react-dom/client'
import { ActivityTracker, TimelinePlot } from '../../src/renderer/features/activity-timeline'
import type { TimelineSeries, TimelineUpdate } from '../../src/renderer/features/activity-timeline-model'
import '../../src/renderer/themes/avalon.css'

// The frozen plot tests supply Series directly. This host changes only its
// available layout space; all marks, labels and interactions are production UI.
const query = new URLSearchParams(location.search)
const kind = query.get('kind')!,
  mode = query.get('mode')!,
  width = Number(query.get('width'))
const date = (month: number, day = 1) => Date.UTC(2026, month - 1, day)
const series: TimelineSeries = {
  start: date(1),
  end: date(9, 8),
  bars: [],
  coverage: [],
  lastPlayed: null,
  summary: 'Recorded activity',
  coverageNote: 'No monthly record',
  periodLabel: 'Hours per month',
}
if (kind === 'months')
  series.bars = [
    { start: date(2), end: date(3), hours: 2, tracked: false, label: 'February · 2h · monthly history' },
    { start: date(7), end: date(8), hours: 4, tracked: true, label: 'July · 4h · Winnow sessions' },
  ]
if (kind === 'sessions')
  series.bars = [
    { start: date(6), end: date(6) + 7200000, hours: 2, tracked: true, label: 'First session' },
    { start: date(6) + 10800000, end: date(6) + 14400000, hours: 1, tracked: true, label: 'Second session' },
    { start: date(8), end: date(8) + 14400000, hours: 4, tracked: true, label: 'Third session' },
  ]
const updates: TimelineUpdate[] = (
  kind === 'updates'
    ? Array.from({ length: 30 }, (_, index) => date(8) + index * 60000)
    : kind === 'narrow'
      ? [date(1), date(9, 8)]
      : []
).map((time, index) => ({
  id: index + 1,
  releaseId: 1,
  kind: 'announcement',
  occurredAt: new Date(time).toISOString(),
  title: 'Patch',
  unread: true,
}))

function Probe() {
  const [current, setCurrent] = useState(updates)
  const [selected, setSelected] = useState('')
  const [requests, setRequests] = useState(0)
  useEffect(() => {
    const acknowledge = () =>
      setCurrent((previous) => previous.map((update) => ({ ...update, unread: false })))
    addEventListener('activity-probe-acknowledge', acknowledge)
    return () => removeEventListener('activity-probe-acknowledge', acknowledge)
  }, [])
  return (
    <div
      className={`avalon-shell ${mode}`}
      style={
        {
          display: 'block',
          padding: 0,
          width,
          height: kind === 'range' ? 600 : 200,
          overflow: 'auto',
          '--avalon-flare': 'HotPink',
        } as CSSProperties
      }
    >
      {kind === 'range' ? (
        <ActivityTracker
          snapshots={[
            {
              ownershipId: 1,
              observedAt: new Date(Date.UTC(2022, 3, 1) - 1000).toISOString(),
              playtimeMinutes: 360,
            },
            {
              ownershipId: 1,
              observedAt: new Date(Date.UTC(2022, 4, 1) - 1000).toISOString(),
              playtimeMinutes: 600,
            },
            {
              ownershipId: 1,
              observedAt: new Date(Date.UTC(2022, 5, 1) - 1000).toISOString(),
              playtimeMinutes: 780,
            },
          ]}
          sessions={[
            {
              id: 1,
              ownershipId: 1,
              startedAt: new Date(date(9, 8) - 20 * 86400000).toISOString(),
              endedAt: new Date(date(9, 8) - 20 * 86400000 + 3600000).toISOString(),
              durationSeconds: 3600,
              detectionMethod: 'process',
            },
            {
              id: 2,
              ownershipId: 1,
              startedAt: new Date(date(9, 8) - 15 * 86400000).toISOString(),
              endedAt: new Date(date(9, 8) - 15 * 86400000 + 7200000).toISOString(),
              durationSeconds: 7200,
              detectionMethod: 'process',
            },
          ]}
          acquiredAt="2020-09-08T00:00:00.000Z"
          lastPlayedAt="2026-08-24T00:00:00.000Z"
          totalMinutes={960}
          now={date(9, 8)}
          scopeKey="1"
          rangeKey={`probe:${mode}:${width}`}
          updates={[]}
          mode={mode as 'desktop' | 'fullscreen'}
        />
      ) : (
        <div className={`activity-tracker mode-${mode}`} style={{ width }}>
          <TimelinePlot
            series={series}
            updates={current}
            tracked={kind === 'sessions'}
            onSelect={setSelected}
            onTracked={() => setRequests((value) => value + 1)}
          />
        </div>
      )}
      <output hidden id="probe-selection">
        {selected}
      </output>
      <output hidden id="probe-requests">
        {requests}
      </output>
    </div>
  )
}

document.documentElement.style.setProperty('--font-body', 'Avalon Body')
document.documentElement.style.setProperty('--font-mono', 'Avalon Data')
document.documentElement.style.setProperty('--accent', 'Aquamarine')
document.documentElement.style.setProperty('--line', 'DarkSlateGray')
document.documentElement.style.setProperty('--muted', 'LightGray')
document.documentElement.style.setProperty('--text', 'LightGray')
document.documentElement.style.setProperty('--surface', 'Black')
document.body.style.minWidth = '0'
createRoot(document.getElementById('root')!).render(<Probe />)

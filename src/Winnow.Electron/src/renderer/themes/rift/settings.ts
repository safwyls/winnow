import type { ThemeSetting } from '../../../shared/theme'

export const riftSettings: ThemeSetting[] = [
  { id: 'portalRoundness', label: 'Portal roundness', type: 'range', default: 60, min: 0, max: 100 },
  { id: 'portalWaviness', label: 'Portal edge shape', type: 'range', default: 42, min: 0, max: 100 },
  {
    id: 'portalActivity',
    label: 'Portal edge activity',
    description: 'Zero holds the edge still. Higher values make the edge move faster.',
    type: 'range',
    default: 40,
    min: 0,
    max: 100,
  },
  { id: 'stars', label: 'Star field', type: 'toggle', default: true },
  { id: 'starBrightness', label: 'Star brightness', type: 'range', default: 65, min: 0, max: 100 },
  {
    id: 'coverSize',
    label: 'Cover size',
    type: 'select',
    default: 'balanced',
    options: [
      { value: 'compact', label: 'Compact' },
      { value: 'balanced', label: 'Balanced' },
      { value: 'large', label: 'Large' },
    ],
  },
]

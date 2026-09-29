import { defineConfig } from '@playwright/test'
export default defineConfig({
  testDir: './tests/electron',
  workers: 1,
  fullyParallel: false,
  timeout: 60000,
  expect: { timeout: 15000 },
  outputDir: '../../.tmp/electron-rendered-results',
  reporter: [['list'], ['json', { outputFile: '../../.tmp/electron-rendered-results/results.json' }]],
})

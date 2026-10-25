import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './tests/packaged',
  workers: 1,
  fullyParallel: false,
  timeout: 60000,
  expect: { timeout: 15000 },
  outputDir: process.env.WINNOW_PACKAGED_RESULTS ?? '../../.tmp/electron-packaged-results',
  reporter: [
    ['list'],
    [
      'json',
      {
        outputFile: `${process.env.WINNOW_PACKAGED_RESULTS ?? '../../.tmp/electron-packaged-results'}/results.json`,
      },
    ],
  ],
})

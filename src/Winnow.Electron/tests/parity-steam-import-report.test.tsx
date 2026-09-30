// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ApiRequest, SteamCaptureResult } from '../src/shared/bridge'
import { SteamPageImport } from '../src/renderer/features/SteamAccountImport'
import { SteamCapture, SteamCaptureReview } from '../src/renderer/features/SteamCapture'
import { SteamAccountOperation } from '../src/renderer/features/SteamAccountOperation'
import { SteamImportReport, type SteamImportReportData } from '../src/renderer/features/SteamImportReport'

afterEach(cleanup)
const report: SteamImportReportData = {
  licensesOutcome: 'Parsed',
  historyOutcome: 'Parsed',
  licenseRowsParsed: 1247,
  licenseRowsSkippedByParser: 0,
  historyRowsParsed: 318,
  acquisitionsMatched: 906,
  pricesMatched: 214,
  ownershipsFilled: 871,
  ownershipsAlreadyComplete: 35,
  licensesTruncated: false,
  historyTruncated: false,
  skippedBundleRows: 0,
  skippedRefundedRows: 0,
  skippedNonPurchaseRows: 0,
  skippedNonProductRows: 0,
  skippedAmbiguousTitle: 0,
  skippedNoOwnershipMatch: 0,
  skippedConflictingRows: 0,
  transactionFactsRecorded: 14,
  licenseFactsRecorded: 20,
  transactionFactsAlreadyRecorded: 2,
  licenseFactsAlreadyRecorded: 3,
}
const captured: SteamCaptureResult = {
  captureOutcome: 'captured',
  licensesStoppedBecause: 'exhausted',
  historyStoppedBecause: 'exhausted',
  pages: {
    licensesHtml: '<table>licenses</table>',
    historyHtml: '<table>history</table>',
    additionalLicensesHtml: [],
    capturedAt: '2026-09-29T00:00:00Z',
    source: 0,
    steamId: '76561198000000001',
  },
}
function file(name = 'licenses.html') {
  const result = new File(['fixture'], name, { type: 'text/html' })
  Object.defineProperty(result, 'arrayBuffer', {
    value: async () => new TextEncoder().encode('fixture').buffer,
  })
  return result
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { resolve, promise }
}
function loaded(
  files = [{ path: 'licenses.html', outcome: 0, kind: 0, detail: null as string | null }],
  anythingLoaded = true,
) {
  return { files, anythingLoaded, pages: { ...captured.pages, source: 1 } }
}
function fixture(
  mode: string,
  node: React.ReactNode,
  respond: (value: ApiRequest) => unknown = () => report,
  capture = vi.fn(async () => captured),
) {
  const request = vi.fn(async (value: ApiRequest) => ({ ok: true, status: 200, data: await respond(value) }))
  const cancel = vi.fn(async () => true)
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, steamCapturePages: capture, cancelSteamWindow: cancel },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const view = render(
    <QueryClientProvider client={client}>
      <div className={`mode-${mode}`}>
        <SteamAccountOperation>{node}</SteamAccountOperation>
      </div>
    </QueryClientProvider>,
  )
  return { ...view, request, capture, cancel }
}
function selectFiles(files = [file()]) {
  fireEvent.change(screen.getByLabelText('Saved Steam pages'), { target: { files } })
}
async function importPages() {
  const button = screen.getByRole('button', { name: 'Import captured pages' }) as HTMLButtonElement
  await waitFor(() => expect(button.disabled).toBe(false))
  fireEvent.click(button)
}
function captureConsent() {
  fireEvent.click(screen.getByRole('button', { name: 'Capture account pages in Winnow' }))
  fireEvent.click(screen.getByRole('button', { name: 'Agree and import pages' }))
}

describe.each(['desktop', 'fullscreen'])('%s detailed Steam import report', (mode) => {
  const show = (data: Partial<SteamImportReportData> = {}, capture?: SteamCaptureResult) =>
    fixture(mode, <SteamImportReport report={{ ...report, ...data }} capture={capture} />)
  it('Counts_are_grouped_and_in_the_order_the_question_is_asked', () => {
    show({}, captured)
    const rows = screen.getByRole('status').querySelectorAll('dl:first-of-type > div')
    expect([...rows].map((row) => row.textContent)).toEqual([
      'Licences found1,247',
      'Purchases found318',
      'Licences matched906',
      'Prices matched214',
      'Games updated871',
      'Already complete35',
    ])
    expect(screen.queryByText(/nothing new was filled/)).toBeNull()
  })
  it('A_licence_total_Steam_does_not_render_is_shown_beside_the_rows_read', () => {
    show({ licenseRowsParsed: 957, licensesReportedTotal: 979, licensesTruncated: true }, captured)
    expect(screen.getByText('Licences reported').parentElement?.textContent).toBe('Licences reported979')
    expect(screen.getByText('Licences found').parentElement?.textContent).toBe('Licences found957')
    expect(screen.getByText(/difference alone does not mean/)).toBeTruthy()
    expect(screen.queryByText(/incomplete|did not get all/)).toBeNull()
  })
  it('A_licence_total_that_matches_the_rows_read_is_not_restated', () => {
    show({ licenseRowsParsed: 40, licenseRowsSkippedByParser: 2, licensesReportedTotal: 42 })
    expect(screen.queryByText('Licences reported')).toBeNull()
    expect(screen.queryByText(/difference alone/)).toBeNull()
  })
  it('Only_the_skip_reasons_that_happened_are_listed', () => {
    show({
      skippedBundleRows: 7,
      skippedNonPurchaseRows: 3,
      skippedAmbiguousTitle: 2,
      skippedConflictingRows: 1,
    })
    const skips = screen.getByRole('region', { name: 'Rows not applied' })
    expect([...skips.querySelectorAll('dt')].map((node) => node.textContent)).toEqual([
      'Bundle purchases',
      'Gifts and in-game purchases',
      'Ambiguous titles',
      'Disagreeing rows',
    ])
    expect([...skips.querySelectorAll('dd')].map((node) => node.textContent)).toEqual(['7', '3', '2', '1'])
    expect(within(skips).queryByText('Refunded purchases')).toBeNull()
  })
  it('A_pass_that_filled_nothing_says_so_rather_than_showing_bare_zeroes', () => {
    show({ ownershipsFilled: 0 })
    expect(screen.getByText('Every row was read but nothing new was filled in.')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.queryByRole('region', { name: 'Rows not applied' })).toBeNull()
  })
  it.each([null, 'no licenses table'])('An_unrecognised_page_reports_the_parser_s_reason: %s', (reason) => {
    show({ licensesOutcome: 'NotRecognized', licensesFailureReason: reason })
    expect(screen.getByRole('alert').textContent).toContain('The licences page was not recognized.')
    if (reason) expect(screen.getByRole('alert').textContent).toContain(reason)
    expect(screen.getByText('Purchases found').parentElement?.textContent).toContain('318')
  })
  it('A_saved_history_page_that_stopped_at_the_first_screen_says_so', () => {
    show({ historyTruncated: true, licenseRowsParsed: 12 })
    expect(screen.getByText(/Steam only saves the purchase history currently shown/).textContent).toContain(
      'Load more transactions',
    )
  })
  it('The_sign_in_route_reports_truncation_without_the_saved_file_remedy', () => {
    show(
      { historyTruncated: true, licensesTruncated: true },
      { ...captured, licensesStoppedBecause: 'stalled', historyStoppedBecause: 'stalled' },
    )
    expect(screen.getAllByText(/This run did not get all/)).toHaveLength(2)
    expect(screen.queryByText(/Load more transactions|saved file/)).toBeNull()
  })
  it('A_walk_that_ran_to_the_end_outranks_a_parser_claiming_truncation', () => {
    show(
      { historyTruncated: true, licensesTruncated: true, licenseRowsParsed: 957, licensesReportedTotal: 979 },
      captured,
    )
    expect(screen.queryByText(/incomplete|did not get all/)).toBeNull()
  })
  it('A_witness_silences_only_its_own_page', () => {
    show(
      { historyTruncated: true, licensesTruncated: true },
      { ...captured, historyStoppedBecause: 'stalled' },
    )
    expect(screen.getAllByText(/This run did not get all/)).toHaveLength(1)
    expect(screen.getByText(/This run did not get all/).textContent).toContain('purchase history')
  })
  it('A_ceiling_outranks_the_end_of_walk_witness', () => {
    show({}, { ...captured, licensesStoppedBecause: 'cap', historyStoppedBecause: 'cap' })
    expect(screen.getAllByText(/Winnow stopped reading/)).toHaveLength(2)
  })
  it.each(['licenses', 'history'] as const)('A_%s_walk_that_hit_the_ceiling_says_Winnow_stopped', (kind) => {
    show({}, { ...captured, [`${kind}StoppedBecause`]: 'cap' })
    expect(screen.getAllByText(/Winnow stopped reading/)).toHaveLength(1)
    expect(screen.getByText(/Winnow stopped reading/).textContent).toContain(
      kind === 'licenses' ? 'licences' : 'purchase history',
    )
    expect(screen.queryByText(/did not get all/)).toBeNull()
  })
  it.each(['licenses', 'history'] as const)(
    'A_stalled_%s_walk_says_the_run_did_not_get_everything',
    (kind) => {
      show({}, { ...captured, [`${kind}StoppedBecause`]: 'stalled' })
      expect(screen.getAllByText(/This run did not get all/)).toHaveLength(1)
      expect(screen.getByText(/This run did not get all/).textContent).toContain(
        kind === 'licenses' ? 'licences' : 'purchase history',
      )
    },
  )
  it('A_session_that_walked_both_pages_out_says_nothing', () => {
    show({}, captured)
    expect(screen.queryByText(/incomplete|did not get all|Winnow stopped/)).toBeNull()
  })
  it('A_licenses_page_the_paginator_calls_partial_says_the_page_paginates', () => {
    show({ licenseRowsParsed: 100, licensesReportedTotal: 1247, licensesTruncated: true })
    expect(screen.getByText(/Steam paginates licences/)).toBeTruthy()
  })
  it.each([
    [98, 2, true],
    [96, 0, true],
    [89, 0, false],
    [61, 0, false],
  ])('saved page boundary uses parsed=%s plus skipped=%s to warn=%s', (parsed, skipped, warn) => {
    show({ licenseRowsParsed: parsed as number, licenseRowsSkippedByParser: skipped as number })
    expect(!!screen.queryByText(/Steam paginates licences/)).toBe(warn)
  })
  it('The_page_boundary_guess_is_never_applied_to_a_walked_session', () => {
    show({ licenseRowsParsed: 100 }, captured)
    expect(screen.queryByText(/Steam paginates licences/)).toBeNull()
  })
  it('A_licences_page_read_whole_says_nothing_about_pagination', () => {
    show({ licenseRowsParsed: 42, licensesReportedTotal: 42 })
    expect(screen.queryByText(/incomplete|did not get all/)).toBeNull()
  })
})

describe.each(['desktop', 'fullscreen'])('%s Steam capture and saved-file routes', (mode) => {
  it('Opening_the_screen_asks_what_can_run_here_and_starts_nothing', () => {
    const { capture, request } = fixture(
      mode,
      <>
        <SteamCapture />
        <SteamPageImport />
      </>,
    )
    expect(capture).not.toHaveBeenCalled()
    expect(request).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Capture account pages in Winnow' })).toBeTruthy()
    expect((screen.getByLabelText('Saved Steam pages') as HTMLInputElement).disabled).toBe(false)
  })
  it('Consent_is_granted_by_the_command_and_by_nothing_else', async () => {
    const { capture, request } = fixture(mode, <SteamCapture />)
    fireEvent.click(screen.getByRole('button', { name: 'Capture account pages in Winnow' }))
    expect(capture).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Cancel capture' }))
    expect(capture).not.toHaveBeenCalled()
    captureConsent()
    await screen.findByText('Games updated')
    expect(capture).toHaveBeenCalledExactlyOnceWith({ consentGranted: true })
    expect(request).toHaveBeenCalledExactlyOnceWith({ route: 'imports.steam.pages', params: undefined, body: captured.pages })
  })
  it.each(['captured', 'partial'] as const)(
    'imports %s pages automatically after the dedicated capture command consents',
    async (outcome) => {
      const capture = {
        ...captured,
        captureOutcome: outcome,
        ...(outcome === 'partial' ? { pages: { ...captured.pages!, historyHtml: null } } : {}),
      }
      const { request } = fixture(mode, <SteamCapture />, undefined, vi.fn(async () => capture))
      captureConsent()
      await screen.findByText('Games updated')
      expect(request).toHaveBeenCalledExactlyOnceWith({
        route: 'imports.steam.pages', params: undefined, body: capture.pages,
      })
      expect(screen.queryByRole('button', { name: 'Import captured pages' })).toBeNull()
    },
  )
  it.each(['saved', 'captured'] as const)(
    'a failed %s import retains its pages for an explicit retry without recapturing',
    async (source) => {
      let imports = 0
      const { request, capture } = fixture(mode, <><SteamCapture /><SteamPageImport /></>, (input) => {
        if (input.route === 'imports.steam.load') return loaded()
        if (++imports === 1) throw new Error('The importer is temporarily unavailable.')
        return report
      })
      if (source === 'saved') selectFiles()
      else captureConsent()
      await screen.findByRole('alert')
      const retry = await screen.findByRole<HTMLButtonElement>('button', { name: 'Retry import' })
      await waitFor(() => expect(retry.disabled).toBe(false))
      fireEvent.click(retry)
      await screen.findByText('Games updated')
      expect(request.mock.calls.filter(([input]) => input.route === 'imports.steam.pages')).toHaveLength(2)
      expect(capture).toHaveBeenCalledTimes(source === 'saved' ? 0 : 1)
      expect(screen.queryByRole('alert')).toBeNull()
    },
  )
  it.each(['captured', 'partial'] as const)(
    'reviews %s pages and imports their source and identity only after confirmation',
    async (outcome) => {
      const capture = {
        ...captured,
        captureOutcome: outcome,
        ...(outcome === 'partial' ? { pages: { ...captured.pages!, historyHtml: null } } : {}),
      }
      const { request } = fixture(mode, <SteamCaptureReview capture={capture} discard={vi.fn()} />)
      expect(request).not.toHaveBeenCalled()
      await importPages()
      await screen.findByText('Licences matched')
      expect(request).toHaveBeenCalledExactlyOnceWith({
        route: 'imports.steam.pages',
        params: undefined,
        body: capture.pages,
      })
      expect(screen.getByText('Games updated').parentElement?.textContent).toBe('Games updated871')
      expect(screen.queryByRole('button', { name: 'Import captured pages' })).toBeNull()
    },
  )
  it.each(['cancelled', 'no-session', 'unavailable', 'failed'] as const)(
    'capture outcome %s is distinct and only failed is a problem',
    async (outcome) => {
      const result = { captureOutcome: outcome }
      const { request } = fixture(
        mode,
        <SteamCapture />,
        undefined,
        vi.fn(async () => result),
      )
      captureConsent()
      await screen.findByRole('region', { name: 'Steam capture result' })
      expect(!!screen.queryByRole('alert')).toBe(outcome === 'failed')
      expect(request).not.toHaveBeenCalled()
      expect(screen.queryByRole('button', { name: 'Import captured pages' })).toBeNull()
    },
  )
  it('A_screen_with_no_harvester_registered_says_the_route_cannot_run', () => {
    fixture(
      mode,
      <>
        <SteamCapture />
        <SteamPageImport />
      </>,
    )
    window.winnow.steamCapturePages = undefined
    cleanup()
    render(
      <QueryClientProvider client={new QueryClient()}>
        <SteamCapture />
        <SteamPageImport />
      </QueryClientProvider>,
    )
    expect(screen.getByText(/capture window is unavailable/)).toBeTruthy()
    expect((screen.getByLabelText('Saved Steam pages') as HTMLInputElement).disabled).toBe(false)
  })
  it.each(['capture', 'load', 'import'])(
    'A_route_in_flight_holds_both_buttons while %s runs',
    async (route) => {
      const gate = deferred<unknown>()
      const { capture } = fixture(
        mode,
        <>
          <SteamCapture />
          <SteamPageImport />
        </>,
        (input) =>
          input.route === 'imports.steam.load' ? (route === 'load' ? gate.promise : loaded()) : gate.promise,
        vi.fn(async () => (await gate.promise) as SteamCaptureResult),
      )
      if (route === 'capture') captureConsent()
      else {
        selectFiles()
        if (route === 'import') {
          await screen.findByText('licenses.html: Licence page ready')
        }
      }
      await waitFor(() =>
        expect((screen.getByLabelText('Saved Steam pages') as HTMLInputElement).disabled).toBe(true),
      )
      const button = screen.getByRole('button', {
        name: 'Capture account pages in Winnow',
        hidden: true,
      }) as HTMLButtonElement
      expect(button.disabled).toBe(true)
      await act(async () =>
        gate.resolve(route === 'capture' ? captured : route === 'load' ? loaded() : report),
      )
      await waitFor(() =>
        expect((screen.getByLabelText('Saved Steam pages') as HTMLInputElement).disabled).toBe(false),
      )
      expect(capture).toHaveBeenCalledTimes(route === 'capture' ? 1 : 0)
    },
  )
  it('Dismissing_the_picker_changes_nothing', () => {
    const { request, capture } = fixture(mode, <SteamPageImport />)
    selectFiles([])
    expect(request).not.toHaveBeenCalled()
    expect(capture).not.toHaveBeenCalled()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByText('No pages were selected. Nothing was imported.')).toBeTruthy()
  })
  it('Files_that_are_not_account_pages_are_named_and_nothing_is_imported', async () => {
    const { request } = fixture(mode, <SteamPageImport />, (input) => input.route === 'imports.steam.pages' ? report :
      loaded([{ path: 'other.html', outcome: 3, kind: 0, detail: 'No account table' }], false),
    )
    selectFiles([file('other.html')])
    await screen.findByText(/None of the selected files were recognized/)
    expect(screen.getByRole('listitem').textContent).toBe('other.html: Not recognized — No account table')
    expect(screen.queryByRole('button', { name: 'Retry import' })).toBeNull()
    expect(request).toHaveBeenCalledTimes(1)
  })
  it.each([
    [0, 'Different_licence_pages_are_both_reported_loaded'],
    [1, 'One_file_of_each_kind_raises_no_second_copy_notice'],
  ] as const)('%s %s', async (kind, _name) => {
    fixture(mode, <SteamPageImport />, (input) => input.route === 'imports.steam.pages' ? report :
      loaded([
        { path: 'one.html', outcome: 0, kind: 0, detail: null },
        { path: 'two.html', outcome: 0, kind, detail: null },
      ]),
    )
    selectFiles([file('one.html'), file('two.html')])
    await screen.findByText(/one.html: Licence page ready/)
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
    expect(screen.queryByText(/Duplicate licence pages/)).toBeNull()
  })
  it('reports duplicate and account-mismatched files without hiding successful pages', async () => {
    fixture(mode, <SteamPageImport />, (input) => input.route === 'imports.steam.pages' ? report :
      loaded([
        { path: 'one.html', outcome: 0, kind: 0, detail: null },
        { path: 'two.html', outcome: 4, kind: 0, detail: null },
        { path: 'foreign.html', outcome: 5, kind: 1, detail: null },
      ]),
    )
    selectFiles()
    await screen.findByText(/Duplicate licence pages/)
    expect(screen.getByText('two.html: Already read')).toBeTruthy()
    expect(screen.getByText('foreign.html: Different account')).toBeTruthy()
  })
  it('Both_routes_converge_on_the_same_importer', async () => {
    const { request, capture } = fixture(
      mode,
      <>
        <SteamCapture />
        <SteamPageImport />
      </>,
      (input) => (input.route === 'imports.steam.load' ? loaded() : report),
    )
    selectFiles()
    await screen.findByText('licenses.html: Licence page ready')
    expect(capture).not.toHaveBeenCalled()
    await screen.findByText('Games updated')
    expect(screen.getByText('licenses.html: Licence page ready')).toBeTruthy()
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Capture account pages in Winnow' }) as HTMLButtonElement)
          .disabled,
      ).toBe(false),
    )
    captureConsent()
    await screen.findByRole('region', { name: 'Steam capture result' })
    await screen.findByText('Games updated')
    expect(
      request.mock.calls
        .filter(([value]) => value.route === 'imports.steam.pages')
        .map(([value]) => value.body),
    ).toEqual([loaded().pages, captured.pages])
  })
  it('A_new_attempt_clears_the_previous_report', async () => {
    const next = deferred<unknown>()
    let reads = 0
    fixture(mode, <SteamPageImport />, (input) =>
      input.route === 'imports.steam.load' ? (++reads === 1 ? loaded() : next.promise) : report,
    )
    selectFiles()
    await screen.findByText('licenses.html: Licence page ready')
    await screen.findByText('Games updated')
    await waitFor(() =>
      expect((screen.getByLabelText('Saved Steam pages') as HTMLInputElement).disabled).toBe(false),
    )
    selectFiles([file('next.html')])
    expect(screen.queryByText('Games updated')).toBeNull()
    expect(screen.queryByText('licenses.html: Licence page ready')).toBeNull()
    await act(async () => next.resolve(loaded()))
  })
  it('No_embedded_browser_says_so_and_the_other_route_is_unaffected', async () => {
    const { capture, request } = fixture(
      mode,
      <>
        <SteamCapture />
        <SteamPageImport />
      </>,
      (input) => (input.route === 'imports.steam.load' ? loaded() : report),
      vi.fn(async () => ({ captureOutcome: 'unavailable' as const })),
    )
    captureConsent()
    await screen.findByText(/capture window is unavailable/)
    selectFiles()
    await screen.findByText('licenses.html: Licence page ready')
    await screen.findByText('Games updated')
    expect(capture).toHaveBeenCalledOnce()
    expect(request.mock.calls.filter(([input]) => input.route === 'imports.steam.pages')).toHaveLength(1)
  })
  it('closing the host cancels its native capture and discards a late result', async () => {
    const gate = deferred<SteamCaptureResult>()
    const { unmount, cancel, request } = fixture(
      mode,
      <SteamCapture />,
      undefined,
      vi.fn(() => gate.promise),
    )
    captureConsent()
    unmount()
    expect(cancel).toHaveBeenCalledOnce()
    await act(async () => gate.resolve(captured))
    expect(request).not.toHaveBeenCalled()
  })
})

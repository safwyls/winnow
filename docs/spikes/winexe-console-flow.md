# Spike: WinExe console-flow regression procedure

> **Dated evidence.** Findings describe the builds and services observed on the dates below.
> Current implementation choices are in [the build spec](../../game-library-design.md);
> current interactions and layout are in [the visual spec](../../design-system.md).
> This record is optional background.

Date: 2026-09-06

## Native executable probe

The Release executable at
`C:\Temp\winnow-beta-final\Release\net10.0\Winnow.exe` was started with
`--epic-login --data-dir <new temporary directory>`. Its standard input, output,
and error were redirected by `System.Diagnostics.ProcessStartInfo`; stdin was
kept open without a line. After five seconds, the test process stopped the child
and read the captured output.

The capture contained the Epic sign-in heading, the instruction to open the URL,
and the input prompt. It did not contain stderr. No code was supplied, so the
browser was not opened and no Epic request was made. The temporary data directory
was used only for this probe.

This proves that the native WinExe writes the console flow to a genuine redirected
destination. It does **not** prove the absent-handle attachment path: redirection
is intentionally preserved instead of being replaced with the parent console.

## Absent-handle regression

`ConsoleAuthPromptTests.A_null_WinExe_standard_handle_is_not_mistaken_for_redirection`
checks both `NULL` and `INVALID_HANDLE_VALUE`. The helper must attach only for
those absent Windows stdout handles; it treats a valid pipe or file handle as a
redirected destination.

To exercise that path manually from a Windows terminal, start the WinExe as a
child without redirecting its streams, then confirm that the sign-in prompt is
visible in the same terminal:

```powershell
$data = Join-Path $env:TEMP ('winnow-console-check-' + [guid]::NewGuid())
$process = Start-Process -FilePath 'C:\Program Files\Winnow\Winnow.exe' `
  -ArgumentList @('--epic-login', '--data-dir', $data) -NoNewWindow -PassThru
```

Use the installed executable path if it differs. Confirm the **Sign in to Epic
Games** heading and the first input prompt, then run
`Stop-Process -Id $process.Id` before entering a code. The procedure does not
sign in, open a browser, or alter the real data directory.

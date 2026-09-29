export function browserToolbar(fullscreen: boolean): string {
  return `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'none'; base-uri 'none'; form-action 'none'"><style>
  :root{color-scheme:dark;font: ${fullscreen ? 20 : 14}px system-ui;color:#eeeae1;background:#18191b}*{box-sizing:border-box}body{margin:0;padding:10px 14px}nav{display:flex;align-items:center;gap:10px}a{color:inherit;text-decoration:none;border:1px solid #414248;border-radius:6px;padding:${fullscreen ? '15px 18px' : '9px 12px'};white-space:nowrap}a:focus-visible{outline:3px solid #cfac75;outline-offset:2px}a[aria-disabled=true]{opacity:.4}#address{min-width:0;flex:1;font: .85em ui-monospace,monospace;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}#problem{color:#e7b974;margin:6px 0 0;font-size:.85em}#hints{color:#aaa79f;font-size:.75em;margin-top:5px}</style></head><body><nav aria-label="Browser controls"><a id="back" href="#back">Back</a><a id="forward" href="#forward">Forward</a><span id="address" aria-label="Current address"></span><a href="#external">Open in browser</a><a href="#close">Close</a></nav><p id="problem" role="status"></p>${fullscreen ? '<div id="hints">↑ ↓ Scroll · LB / RB Links · A Open · LT / RT Page · B Close · Menu Browser controls</div>' : ''}</body></html>`
}
export function browserToolbarAction(url: string): 'back' | 'forward' | 'external' | 'close' | null {
  return (
    (/^winnow-browser:\/\/(back|forward|external|close)$/.exec(url)?.[1] as
      'back' | 'forward' | 'external' | 'close' | undefined) ?? null
  )
}

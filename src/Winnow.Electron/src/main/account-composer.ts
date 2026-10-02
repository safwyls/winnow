import { popoutFontFaces } from './popout-typography'

export const accountInputHints =
  '↑ ↓ Field · LB / RB Field · A Select · X Check · Y Type · View Backspace · B Cancel'
const escape = (value: string) =>
  value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;')
export function accountComposerKeys(upper: boolean, symbols: boolean) {
  return [
    ...(symbols ? '1234567890!@#$%^&*()_+-=[]{};:\'",.<>/?\\|`~' : '1234567890qwertyuiopasdfghjklzxcvbnm')
      .split('')
      .map((letter) => (upper ? letter.toUpperCase() : letter)),
    'Space',
    'Backspace',
    'Shift',
    'Symbols',
    'Done',
    'Cancel',
  ]
}
export function accountInputDocument(composing = false, upper = false, symbols = false) {
  const keys = accountComposerKeys(upper, symbols)
  return `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; font-src data:; script-src 'none'; base-uri 'none'; form-action 'none'"><style>${popoutFontFaces}
  :root{font:calc(28px * var(--theme-text-scale,1))/1.5 var(--font-body,"Avalon Body",sans-serif);color:#eeeae1;background:#18191b;color-scheme:dark}*{box-sizing:border-box}body{margin:0;padding:18px 32px}p{margin:0 0 14px}main{max-width:1100px;margin:24px auto}h1{font:700 calc(32px * var(--theme-text-scale,1))/1.2 var(--font-display,"Avalon Display",sans-serif)}input{display:block;width:100%;padding:16px;font:inherit;border:1px solid #777;border-radius:8px;background:#202225;color:inherit;margin:24px 0}nav{display:grid;grid-template-columns:repeat(10,1fr);gap:10px}a{display:block;color:inherit;text-align:center;text-decoration:none;border:1px solid #57585f;border-radius:8px;padding:15px 8px;min-height:64px}a:focus-visible,a[data-selected=true]{outline:3px solid #cfac75;outline-offset:2px}small,.account-input-hints{color:#b9b7b2;font-size:calc(24px * var(--theme-text-scale,1))}#problem{color:#e7b974}</style></head><body>${composing ? `<main><h1>Type into the focused browser field</h1><p>Your draft stays masked. Done inserts it; Cancel discards it.</p><input id="draft" type="password" autocomplete="off" maxlength="4096" aria-label="Text to insert" autofocus><nav aria-label="Account keyboard">${keys.map((key, index) => `<a id="key-${index}" href="#key-${index}" data-selected="${index === 0}">${escape(key)}</a>`).join('')}</nav><p id="problem" role="status"></p><small>A Type · X Backspace · Y Shift · Menu Done · B Discard</small></main>` : `<p class="account-input-hints">${accountInputHints}</p><p id="problem" role="status"></p>`}</body></html>`
}

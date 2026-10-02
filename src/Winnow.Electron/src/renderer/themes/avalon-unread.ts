export function unreadLabel(patched: boolean, count: number): string {
  if (!patched) return ''
  const updates = count > 0 ? `: ${count.toLocaleString()} ${count === 1 ? 'update' : 'updates'}` : ''
  return `, patched since you played${updates}`
}

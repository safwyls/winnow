// Plain anchors and public assets do not receive the framework's basePath automatically.
export function sitePath(path: string): string {
  return `${process.env.NEXT_PUBLIC_BASE_PATH || ''}${path}`;
}

export const releasesUrl = 'https://github.com/safwyls/winnow/releases';
export const releasesApi = 'https://api.github.com/repos/safwyls/winnow/releases?per_page=100';

export const officialPlugins = [
  { id: 'xbox', name: 'Xbox', assembly: 'Xbox', category: 'Library',
    description: 'Bring installed Xbox PC games and optional Xbox played history into your library.',
    setup: 'Local PC discovery needs no sign-in. Connect your Microsoft account for played history.',
    availability: 'PC discovery and protected account sign-in on Windows.' },
  { id: 'psn', name: 'PlayStation', assembly: 'Psn', category: 'Library',
    description: 'Add your PS4 and PS5 account library, with optional played games and PS3 / PS Vita trophy history.',
    setup: 'Connect with a Sony session token in plugin settings. Imported console games have no launch action.',
    availability: 'Protected account connection on Windows.' },
  { id: 'steamgriddb', name: 'SteamGridDB', assembly: 'SteamGridDb', category: 'Artwork',
    description: 'Find community artwork for your games, including covers and backgrounds.',
    setup: 'Already bundled with Winnow. Add your own SteamGridDB API key in plugin settings.',
    availability: 'Windows and Linux. Protected API-key storage on Windows.' },
] as const;

export type PluginId = typeof officialPlugins[number]['id'];
type ReleaseAsset = { name: string; url: string; size: number };
export type PluginDownload = ReleaseAsset & { version: string; installUri: string };
export type PluginRelease = {
  tag: string; prerelease: boolean; url: string;
  downloads: Partial<Record<PluginId, PluginDownload>>;
};
const pluginVersionPattern = '(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)';
const versionPattern = `${pluginVersionPattern}(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?`;
const releasePattern = new RegExp(`^v${versionPattern}$`);
const record = (value: unknown): Record<string, unknown> | null =>
  value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null;

function assetFrom(value: unknown, tag: string): ReleaseAsset | null {
  const asset = record(value);
  if (!asset || typeof asset.name !== 'string' || asset.name.trim() !== asset.name || asset.state !== 'uploaded' ||
      typeof asset.size !== 'number' || !Number.isSafeInteger(asset.size) || asset.size <= 0 || asset.size > 256 * 1024 * 1024 ||
      typeof asset.digest !== 'string' || asset.digest.length !== 71 || !/^sha256:[a-fA-F0-9]{64}$/.test(asset.digest)) return null;
  const url = `${releasesUrl}/download/${tag}/${asset.name}`;
  if (asset.browser_download_url !== url) return null;
  return { name: asset.name, url, size: asset.size };
}

/** GitHub's JSON API is CORS-readable; release binaries need not be fetched by the browser. */
export function selectPluginRelease(value: unknown): PluginRelease | null {
  if (!Array.isArray(value)) throw new Error('GitHub returned an invalid release list.');
  const candidates: { release: PluginRelease; published: number }[] = [];
  for (const item of value.slice(0, 100)) {
    const source = record(item);
    if (!source || source.draft !== false || typeof source.prerelease !== 'boolean' ||
        typeof source.tag_name !== 'string' || source.tag_name.trim() !== source.tag_name || source.tag_name.length > 80 || !releasePattern.test(source.tag_name) ||
        typeof source.published_at !== 'string' || !Array.isArray(source.assets) || source.assets.length > 100) continue;
    const tag = source.tag_name;
    const published = Date.parse(source.published_at);
    if (!Number.isFinite(published) || source.html_url !== `${releasesUrl}/tag/${tag}`) continue;
    const assets = source.assets.map(asset => assetFrom(asset, tag)).filter((asset): asset is ReleaseAsset => asset !== null);
    if (assets.filter(asset => asset.name === 'winnow-plugins.json').length !== 1) continue;
    const downloads: PluginRelease['downloads'] = {};
    for (const plugin of officialPlugins) {
      const pattern = new RegExp(`^Winnow\\.Plugin\\.${plugin.assembly}-(${pluginVersionPattern})\\.zip$`);
      const matches = assets.filter(asset => pattern.test(asset.name));
      if (matches.length !== 1) continue;
      const asset = matches[0];
      downloads[plugin.id] = { ...asset, version: pattern.exec(asset.name)![1],
        installUri: `winnow://plugins/install?id=${plugin.id}&release=${tag}` };
    }
    if (Object.keys(downloads).length === 0) continue;
    candidates.push({ release: { tag, prerelease: source.prerelease, url: source.html_url, downloads }, published });
  }
  candidates.sort((a, b) => Number(a.release.prerelease) - Number(b.release.prerelease) || b.published - a.published);
  return candidates[0]?.release ?? null;
}

export async function fetchPluginRelease(signal: AbortSignal): Promise<PluginRelease | null> {
  const response = await fetch(releasesApi, {
    headers: { Accept: 'application/vnd.github+json' }, signal, credentials: 'omit',
  });
  if (!response.ok) throw new Error('GitHub releases are unavailable.');
  // Bound the public response before parsing, including responses without Content-Length.
  const reader = response.body?.getReader();
  if (!reader) throw new Error('GitHub returned an empty response.');
  const decoder = new TextDecoder();
  let content = '', bytes = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      bytes += value.byteLength;
      if (bytes > 4 * 1024 * 1024) throw new Error('GitHub returned too much release data.');
      content += decoder.decode(value, { stream: true });
    }
    content += decoder.decode();
    return selectPluginRelease(JSON.parse(content));
  } finally {
    await reader.cancel();
    reader.releaseLock();
  }
}

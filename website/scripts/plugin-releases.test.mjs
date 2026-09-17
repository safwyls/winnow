import { test } from 'node:test';
import assert from 'node:assert/strict';
import { selectPluginRelease, fetchPluginRelease, releasesUrl } from '../lib/plugin-releases.ts';

const asset = (tag, name) => ({ name, size: 1024, state: 'uploaded', digest: `sha256:${'a'.repeat(64)}`, browser_download_url: `${releasesUrl}/download/${tag}/${name}` });
const release = (tag = 'v0.2.0', prerelease = false) => ({
  tag_name: tag, html_url: `${releasesUrl}/tag/${tag}`, published_at: '2026-09-16T12:00:00Z', draft: false, prerelease,
  assets: ['winnow-plugins.json', 'Winnow.Plugin.Xbox-1.0.0.zip', 'Winnow.Plugin.Psn-1.0.0.zip', 'Winnow.Plugin.SteamGridDb-1.0.0.zip'].map(name => asset(tag, name)),
});

test('selects all three versioned packages and exact application handoff links', () => {
  const chosen = selectPluginRelease([release()]);
  assert.deepEqual(Object.keys(chosen.downloads), ['xbox', 'psn', 'steamgriddb']);
  assert.equal(chosen.downloads.psn.installUri, 'winnow://plugins/install?id=psn&release=v0.2.0');
  assert.equal(chosen.downloads.psn.version, '1.0.0');
  assert.equal(chosen.downloads.psn.url, `${releasesUrl}/download/v0.2.0/Winnow.Plugin.Psn-1.0.0.zip`);
});
test('prefers stable and selects newest published date within the channel', () => {
  const older = { ...release('v0.1.0'), published_at: '2026-01-01T00:00:00Z' };
  assert.equal(selectPluginRelease([release('v0.3.0-beta.1', true), older, release()]).tag, 'v0.2.0');
  assert.equal(selectPluginRelease([release('v0.3.0-beta.1', true)]).prerelease, true);
});
test('ignores drafts, malformed releases, and legacy releases without a catalogue', () => {
  assert.equal(selectPluginRelease([null, {}, { ...release(), draft: true }, { ...release(), assets: [] }]), null);
  assert.throws(() => selectPluginRelease({}));
  for (const tag of ['v0.2.0/../../x', 'v0.2.0&url=https://evil.invalid', 'v01.2.0', 'v0.2.0%0A', 'v0.2.0\n']) {
    assert.equal(selectPluginRelease([release(tag)]), null);
  }
});
test('only offers uploaded, bounded, hashed assets from the exact release', () => {
  for (const changes of [
    { browser_download_url: 'https://evil.invalid/plugin.zip' },
    { browser_download_url: `${releasesUrl}/download/v0.1.0/Winnow.Plugin.Psn-1.0.0.zip` },
    { state: 'new' }, { digest: null }, { digest: 'sha256:abc' }, { size: 0 }, { size: 2 ** 30 }, { size: 1.5 },
  ]) {
    const candidate = release();
    candidate.assets[2] = { ...candidate.assets[2], ...changes };
    assert.equal(selectPluginRelease([candidate]).downloads.psn, undefined);
  }
});
test('ambiguous versions are unavailable and a malformed catalogue disables the release', () => {
  const candidate = release();
  candidate.assets.push(asset('v0.2.0', 'Winnow.Plugin.Psn-2.0.0.zip'));
  assert.equal(selectPluginRelease([candidate]).downloads.psn, undefined);
  candidate.assets[0].digest = null;
  assert.equal(selectPluginRelease([candidate]), null);
});
test('fetch handles release data, HTTP failures, and oversized streams', async t => {
  t.mock.method(globalThis, 'fetch', async () => new Response(JSON.stringify([release()])));
  assert.equal((await fetchPluginRelease(new AbortController().signal)).tag, 'v0.2.0');
  globalThis.fetch.mock.mockImplementation(async () => new Response('', { status: 403 }));
  await assert.rejects(fetchPluginRelease(new AbortController().signal), /unavailable/);
  globalThis.fetch.mock.mockImplementation(async () => new Response('x'.repeat(4 * 1024 * 1024 + 1)));
  await assert.rejects(fetchPluginRelease(new AbortController().signal), /too much/);
});

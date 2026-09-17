'use client';

import { useEffect, useState } from 'react';
import { Download, ExternalLink, Gamepad2, Image, RefreshCw } from 'lucide-react';
import { fetchPluginRelease, officialPlugins, releasesUrl, type PluginRelease } from '@/lib/plugin-releases';

type ReleaseState = { status: 'loading' | 'error' | 'ready'; release: PluginRelease | null };

export default function PluginCatalogue() {
  const [state, setState] = useState<ReleaseState>({ status: 'loading', release: null });
  const [attempt, setAttempt] = useState(0);
  const [handoff, setHandoff] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    const timeout = setTimeout(() => controller.abort(), 15_000);
    setState({ status: 'loading', release: null });
    fetchPluginRelease(controller.signal)
      .then(release => { if (active) setState({ status: 'ready', release }); })
      .catch(() => { if (active) setState({ status: 'error', release: null }); })
      .finally(() => clearTimeout(timeout));
    return () => { active = false; clearTimeout(timeout); controller.abort(); };
  }, [attempt]);

  const release = state.release;
  return <>
    <div className="plugins-release" aria-live="polite" aria-atomic="true">
      {state.status === 'loading' ? <p>Checking GitHub for plugin downloads…</p>
        : state.status === 'error' ? <><p>Couldn’t load downloads from GitHub. Try again or <a href={releasesUrl}>browse releases</a>.</p><button onClick={() => setAttempt(value => value + 1)}><RefreshCw size={16} aria-hidden="true" /> Retry</button></>
        : release ? <p><span className={release.prerelease ? 'plugins-channel beta' : 'plugins-channel'}>{release.prerelease ? 'Pre-release' : 'Stable'}</span> From <a href={release.url}>Winnow {release.tag.slice(1)}</a>{release.prerelease && ' · These plugins are part of a preview release.'}</p>
        : <p>Plugin downloads will appear here with the next release that includes them. <a href={releasesUrl}>Browse Winnow releases ↗</a></p>}
    </div>
    <noscript><p className="plugins-noscript">Enable JavaScript to load the latest download links, or <a href={releasesUrl}>download plugins from GitHub releases</a>.</p></noscript>
    <div className="plugin-grid">
      {officialPlugins.map(plugin => {
        const download = release?.downloads[plugin.id];
        const Icon = plugin.id === 'steamgriddb' ? Image : Gamepad2;
        return <article className={`plugin-card plugin-${plugin.id}`} key={plugin.id} aria-labelledby={`plugin-${plugin.id}`}>
          <div className="plugin-heading"><Icon size={30} strokeWidth={1.5} aria-hidden="true" /><span>{plugin.category}</span></div>
          <h2 id={`plugin-${plugin.id}`}>{plugin.name}</h2>
          <p className="plugin-description">{plugin.description}</p>
          <div className="plugin-actions">
            {download ? <>
              <p className="plugin-version">Version {download.version} <span>· {(download.size / (1024 * 1024)).toFixed(1)} MB</span></p>
              <a className="button button-primary" href={download.installUri} aria-label={`Install ${plugin.name} in Winnow`} onClick={() => setHandoff(plugin.name)}><ExternalLink size={17} aria-hidden="true" /> Install in Winnow</a>
              <a className="plugin-zip" href={download.url} aria-label={`Download ${plugin.name} ZIP`}><Download size={17} aria-hidden="true" /> Download ZIP</a>
            </> : <p className="plugin-unavailable">{state.status === 'loading' ? 'Finding downloads…' : state.status === 'error' ? 'Downloads unavailable' : release ? 'Not included in this release' : 'Awaiting a plugin release'}</p>}
          </div>
          <p className="plugin-setup">{plugin.setup}</p>
          <p className="plugin-availability">{plugin.availability}</p>
        </article>;
      })}
    </div>
    <p className="plugin-handoff" role="status">{handoff && <>Continue in Winnow to set up {handoff}. If the app didn’t open, install the latest Winnow or use the ZIP download.</>}</p>
  </>;
}

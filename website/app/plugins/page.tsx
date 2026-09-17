import type { Metadata } from 'next';
import { sitePath } from '@/lib/site-path';
import { releasesUrl } from '@/lib/plugin-releases';
import PluginCatalogue from './PluginCatalogue';
import './plugins.css';

export const metadata: Metadata = {
  title: 'Plugins — Winnow',
  description: 'Add Xbox, PlayStation, and SteamGridDB to Winnow. Install official plugins from your browser or download a ZIP.',
};

export default function Plugins() {
  return <div className="plugins-page">
    <a className="plugins-skip" href="#plugins-content">Skip to plugins</a>
    <header className="site-header plugins-header">
      <a className="brand" href={sitePath('/')} aria-label="Winnow home"><span className="brand-mark" aria-hidden="true" /><span>Winnow</span></a>
      <nav aria-label="Primary navigation">
        <a href={sitePath('/plugins/')} aria-current="page">Plugins</a>
        <a href={sitePath('/docs/')}>Docs</a>
        <a className="nav-cta" href={releasesUrl}>Download Winnow</a>
      </nav>
    </header>
    <main id="plugins-content" className="shell" tabIndex={-1}>
      <div className="plugins-intro">
        <p className="eyebrow">Made for Winnow</p>
        <h1>Plugins</h1>
        <p>Add console libraries and more artwork. Choose a plugin to install in Winnow, or download its ZIP.</p>
      </div>
      <PluginCatalogue />
      <section className="plugins-help" aria-labelledby="install-help">
        <div><h2 id="install-help">From browser to library.</h2><p><strong>Install in Winnow</strong> opens the app, installs the plugin, and takes you to its settings. Your browser may ask to open Winnow.</p><p>Use a current Winnow installation. Windows installers and Linux packages register the browser handoff; portable copies may need a file association.</p></div>
        <div><h3>Prefer the ZIP?</h3><p>Open <strong>Settings → Plugins → Open plugins folder</strong>, copy in the ZIP, and restart Winnow. Enable the plugin and restart again.</p><a className="text-link" href={sitePath('/docs/plugins/')}>Read the installation guide ↗</a><h3>Building a provider?</h3><a className="text-link" href={sitePath('/docs/plugin-sdk/')}>Explore the plugin SDK ↗</a></div>
      </section>
    </main>
    <footer><a className="brand" href={sitePath('/')}><span className="brand-mark" aria-hidden="true" /><span>Winnow</span></a><p>Your library. Your data. Your next game.</p><div><a href={sitePath('/docs/')}>Docs</a><a href={sitePath('/developers/')}>Developers</a><a href="https://github.com/safwyls/winnow">GitHub</a></div></footer>
  </div>;
}

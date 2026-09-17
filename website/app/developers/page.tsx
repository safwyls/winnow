import { sitePath } from '@/lib/site-path';
import type { Metadata } from 'next';
import { ArrowUpRight, Code2, Layers3, LockKeyhole, Radar, Workflow } from 'lucide-react';

export const metadata: Metadata = {
  title: 'Developers — Winnow',
  description: 'Explore Winnow’s local-first architecture, module boundaries, technology stack, and contribution workflow.',
};

const modules = [
  ['Winnow.Core', 'Domain records and contracts. BCL only.'],
  ['Winnow.Data', 'SQLite, Dapper, migrations, and derived bucket queries.'],
  ['Winnow.Ingest.*', 'Read-only Steam, Epic, and GOG local-file readers.'],
  ['Winnow.Resolve', 'Hard joins, soft-match queue, human confirmation.'],
  ['Winnow.Enrich.*', 'Rate-limited, cached, soft-failing metadata clients.'],
  ['Winnow.Recommend', 'Explainable scoring over longitudinal play signals.'],
  ['Winnow.Monitor', 'Process discovery and session recording.'],
  ['Winnow.App', 'Avalonia UI and generic-host composition root.'],
];

export default function Developers() {
  return (
    <main className="dev-page">
      <header className="site-header dev-header">
        <a className="brand" href={sitePath('/')} aria-label="Winnow home"><span className="brand-mark" aria-hidden="true" /><span>Winnow</span><span className="dev-slash">/ developers</span></a>
        <nav aria-label="Developer navigation"><a href="#architecture">Architecture</a><a href={sitePath('/docs/plugin-sdk/')}>Plugin SDK</a><a className="nav-docs" href={sitePath('/docs/')}>Docs</a><a className="nav-cta" href="https://github.com/safwyls/winnow"><Code2 size={15} aria-hidden="true" /> GitHub</a></nav>
      </header>

      <section className="dev-hero shell">
        <div>
          <p className="eyebrow">Free and open source</p>
          <h1>Build on Winnow.</h1>
        </div>
        <div className="dev-intro">
          <p>Winnow combines launcher files, game metadata, and play history in a local desktop application. Explore the code, add a provider, or help improve the experience on Windows and Linux.</p>
          <div className="stack-line"><span>.NET 10</span><span>Avalonia 11</span><span>SQLite</span><span>xUnit</span></div>
        </div>
      </section>

      <section className="principles shell" aria-label="Engineering principles">
        <article><LockKeyhole aria-hidden="true" /><strong>Local data</strong><p>State lives in the user’s data directory. Store files are read, never written.</p></article>
        <article><Radar aria-hidden="true" /><strong>Optional network services</strong><p>Metadata is fetched in the background and cached. Unavailable providers do not prevent the library from opening.</p></article>
        <article><Workflow aria-hidden="true" /><strong>Review uncertain matches</strong><p>External IDs auto-merge. Fuzzy identity matches always wait for a person.</p></article>
        <article><Layers3 aria-hidden="true" /><strong>Explainable recommendations</strong><p>The recommender returns the signals behind each pick, not only a score.</p></article>
      </section>

      <section className="architecture-section" id="architecture">
        <div className="shell architecture-heading">
          <div><p className="eyebrow">Runtime architecture</p><h2>How the app fits together.</h2></div>
          <p>The Avalonia UI reads one SQLite library and raises commands. Local ingest, identity resolution, session monitoring, enrichment, and recommendations run behind interfaces under the same generic host.</p>
        </div>
        <figure className="architecture-figure shell">
          <a href={sitePath('/architecture-diagram.html')} aria-label="Open the architecture diagram with zoom and component details">
            <img src={sitePath('/assets/architecture-overview.svg')} width="1360" height="650" loading="lazy" alt="Winnow runtime: the Avalonia UI and background services share a local SQLite library. Read-only launcher readers feed identity resolution; external providers supply metadata and covers." />
          </a>
          <figcaption className="diagram-link"><a href={sitePath('/architecture-diagram.html')}>Explore with zoom and component details <ArrowUpRight size={16} aria-hidden="true" /></a><span>Architecture snapshot · September 2026</span></figcaption>
        </figure>
      </section>

      <section className="module-section shell" id="modules">
        <div className="module-heading"><p className="eyebrow">Module map</p><h2>Where to make a change.</h2><p>The domain layer has no IO dependencies. Separate assemblies handle storage, platform readers, metadata, and presentation.</p></div>
        <div className="module-grid">
          {modules.map(([name, description], index) => <article key={name}><span>{String(index + 1).padStart(2, '0')}</span><h3>{name}</h3><p>{description}</p></article>)}
        </div>
      </section>

      <section className="build-section shell">
        <div><p className="eyebrow">Build it</p><h2>Two commands from source.</h2><p>No cloud setup, service account, or local container stack.</p></div>
        <pre aria-label="Build and test commands"><code><span>PS</span> dotnet build{`\n`}<span>PS</span> dotnet test</code></pre>
        <a className="button button-primary" href="https://github.com/safwyls/winnow"><Code2 size={18} aria-hidden="true" /> Browse the source</a>
      </section>

      <footer><a className="brand" href={sitePath('/')}><span className="brand-mark" aria-hidden="true" /><span>Winnow</span></a><p>Local-first game discovery, built in the open.</p><div><a href={sitePath('/docs/')}>Docs</a><a href={sitePath('/')}>For players</a><a href="https://github.com/safwyls/winnow">GitHub</a></div></footer>
    </main>
  );
}

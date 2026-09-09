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
        <nav aria-label="Developer navigation"><a href="#architecture">Architecture</a><a href="#modules">Modules</a><a className="nav-cta" href="https://github.com/safwyls/winnow"><Code2 size={15} aria-hidden="true" /> GitHub</a></nav>
      </header>

      <section className="dev-hero shell">
        <div>
          <p className="eyebrow">LOCAL PROCESS · LOCAL DATA · EXPLICIT BOUNDARIES</p>
          <h1>Sharp software for a fuzzy problem.</h1>
        </div>
        <div className="dev-intro">
          <p>Winnow turns scattered launcher files, public metadata, and years of play history into one trustworthy model—without a service tier or user account.</p>
          <div className="stack-line"><span>.NET 10</span><span>Avalonia 11</span><span>SQLite</span><span>xUnit</span></div>
        </div>
      </section>

      <section className="principles shell" aria-label="Engineering principles">
        <article><LockKeyhole aria-hidden="true" /><strong>Local is the boundary</strong><p>State lives in the user’s data directory. Store files are read, never written.</p></article>
        <article><Radar aria-hidden="true" /><strong>Background means background</strong><p>Enrichment is cached and soft-failing. No network client sits on first paint.</p></article>
        <article><Workflow aria-hidden="true" /><strong>Precision over recall</strong><p>External IDs auto-merge. Fuzzy identity matches always wait for a person.</p></article>
        <article><Layers3 aria-hidden="true" /><strong>Reasons are output</strong><p>The recommender returns the signals behind each pick, not only a score.</p></article>
      </section>

      <section className="architecture-section" id="architecture">
        <div className="shell architecture-heading">
          <div><p className="eyebrow">Runtime architecture</p><h2>One process.<br />Hard edges.</h2></div>
          <p>The Avalonia UI reads one SQLite library and raises commands. Local ingest, identity resolution, session monitoring, enrichment, and recommendations run behind interfaces under the same generic host.</p>
        </div>
        <div className="diagram-frame shell">
          <iframe title="Interactive Winnow runtime architecture diagram" src={sitePath('/architecture-diagram.html')} loading="lazy" />
        </div>
        <div className="shell diagram-link"><a href={sitePath('/architecture-diagram.html')}>Open the full interactive diagram <ArrowUpRight size={16} aria-hidden="true" /></a><span>Built and validated with Archify</span></div>
      </section>

      <section className="module-section shell" id="modules">
        <div className="module-heading"><p className="eyebrow">Module map</p><h2>Each assembly gets one job.</h2><p>Dependencies point inward. IO stays at the edges. Derived facts remain queries, never duplicated source-of-truth columns.</p></div>
        <div className="module-grid">
          {modules.map(([name, description], index) => <article key={name}><span>{String(index + 1).padStart(2, '0')}</span><h3>{name}</h3><p>{description}</p></article>)}
        </div>
      </section>

      <section className="build-section shell">
        <div><p className="eyebrow">Build it</p><h2>Two commands from source.</h2><p>No cloud setup, service account, or local container stack.</p></div>
        <pre aria-label="Build and test commands"><code><span>PS</span> dotnet build{`\n`}<span>PS</span> dotnet test</code></pre>
        <a className="button button-primary" href="https://github.com/safwyls/winnow"><Code2 size={18} aria-hidden="true" /> Browse the source</a>
      </section>

      <footer><a className="brand" href={sitePath('/')}><span className="brand-mark" aria-hidden="true" /><span>Winnow</span></a><p>Local-first game discovery, built in the open.</p><div><a href={sitePath('/')}>For players</a><a href="https://github.com/safwyls/winnow">GitHub</a></div></footer>
    </main>
  );
}

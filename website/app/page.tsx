import { sitePath } from '@/lib/site-path';
import { ArrowRight, Download, ShieldCheck } from 'lucide-react';
import StoryDemos from './StoryDemos';

const releasesUrl = 'https://github.com/safwyls/winnow/releases';

function Brand() {
  return (
    <a className="brand" href="#top" aria-label="Winnow home">
      <span className="brand-mark" aria-hidden="true" />
      <span>Winnow</span>
    </a>
  );
}

export default function Home() {
  return (
    <main id="top">
      <header className="site-header">
        <Brand />
        <nav aria-label="Primary navigation">
          <a href="#why">Why Winnow</a>
          <a href={sitePath('/developers/')}>For developers</a>
          <a className="nav-cta" href={releasesUrl}>Download</a>
        </nav>
      </header>

      <section className="hero shell" aria-labelledby="hero-title">
        <div className="hero-copy">
          <p className="eyebrow"><span className="patch-dot" /> Your library has unread mail</p>
          <h1 id="hero-title">Find the game you meant to play.</h1>
          <p className="hero-lede">Winnow brings Steam, Epic, and GOG into one quiet library—and surfaces the games you own, forgot, or left just before they got better.</p>
          <div className="hero-actions">
            <a className="button button-primary" href={releasesUrl}><Download size={18} aria-hidden="true" /> Download Winnow</a>
            <a className="text-link" href="#how-it-works">See how it works <ArrowRight size={17} aria-hidden="true" /></a>
          </div>
          <p className="local-note"><ShieldCheck size={16} aria-hidden="true" /> Local-first. No account. No telemetry.</p>
        </div>

        <div className="library-stage" aria-label="Winnow library showing dormant games as faded cover art">
          <div className="screenshot-crop"><img src={sitePath('/assets/library-grid.png')} alt="A game cover grid with older, dormant games visibly faded" /></div>
        </div>
      </section>

      <section className="proof-strip" aria-label="Winnow at a glance">
        <div><strong>3 platforms</strong><span>one library</span></div>
        <div><strong>0 uploads</strong><span>your history stays yours</span></div>
        <div><strong>Every pick explained</strong><span>no black-box ranking</span></div>
      </section>

      <section className="story shell" id="why">
        <div className="story-heading"><p className="eyebrow">A backlog that can speak</p><h2>Not another shelf.<br />A reason to return.</h2></div>
        <StoryDemos />
      </section>

      <section className="privacy shell" id="how-it-works">
        <div className="privacy-copy"><p className="eyebrow">Your machine is the cloud</p><h2>Your library stays where you left it.</h2><p>Winnow reads your platforms’ local files, keeps its database and cover cache on your computer, and never writes back to Steam, Epic, GOG, or any other external platform.</p></div>
        <div className="privacy-path" aria-label="Local platform data and IGDB metadata flow into Winnow; there is no path to someone else’s computer">
          <div><span className="mono">STEAM</span><span className="mono">EPIC</span><span className="mono">GOG</span></div><span className="path-line" aria-hidden="true" /><div className="winnow-node"><div className="metadata-source"><span>IGDB</span><small>metadata</small></div><div className="device-card"><span className="brand-mark" aria-hidden="true" /><strong>Winnow</strong><small>SQLite + covers<br />on this computer</small></div></div><span className="blocked-path" aria-hidden="true"><span>×</span></span><div className="cloud-ghost"><span>someone else’s computer</span></div>
        </div>
      </section>

      <section className="closing shell">
        <p className="eyebrow">Ready when your library is</p><h2>There’s something good in there.</h2>
        <a className="button button-primary" href={releasesUrl}><Download size={18} aria-hidden="true" /> Download for Windows or Linux</a>
        <p>Free during beta · source available on GitHub</p>
      </section>

      <footer><Brand /><p>Local-first game discovery for the library you already own.</p><div><a href={sitePath('/developers/')}>Developers</a><a href="https://github.com/safwyls/winnow">GitHub</a></div></footer>
    </main>
  );
}

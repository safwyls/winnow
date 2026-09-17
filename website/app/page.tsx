import { sitePath } from '@/lib/site-path';
import { ArrowRight, Download, ShieldCheck } from 'lucide-react';
import StoryDemos from './StoryDemos';
import HeroCarousel from './HeroCarousel';
import './promo.css';

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
    <main id="top" className="player-page">
      <header className="site-header">
        <Brand />
        <nav aria-label="Primary navigation">
          <a href="#why">Why Winnow</a>
          <a href="#how-it-works">How it works</a>
          <a href={sitePath('/developers/')}>For developers</a>
          <a className="nav-docs" href={sitePath('/docs/')}>Docs</a>
          <a className="nav-cta" href={releasesUrl}>Download</a>
        </nav>
      </header>

      <section className="hero shell" aria-labelledby="hero-title">
        <div className="hero-copy">
          <p className="eyebrow">For your Steam, Epic &amp; GOG games</p>
          <h1 id="hero-title">Find the game you meant to play.</h1>
          <p className="hero-lede">Bring your PC games into one library. See what you haven’t played, what’s been updated, and what might be worth picking up again.</p>
          <div className="hero-actions">
            <a className="button button-primary" href={releasesUrl}><Download size={18} aria-hidden="true" /> Download Winnow</a>
            <a className="text-link" href="#how-it-works">See how it works <ArrowRight size={17} aria-hidden="true" /></a>
          </div>
          <p className="local-note"><ShieldCheck size={16} aria-hidden="true" /> No Winnow account. No telemetry. Your data stays on your PC.</p>
        </div>

        <HeroCarousel />
      </section>

      <div className="platform-line shell" aria-label="Availability">
        <span>Windows &amp; Linux</span><span>Mouse, keyboard &amp; controller</span><span>Free &amp; open source</span>
      </div>

      <section className="story shell" id="why">
        <div className="section-heading"><div><p className="eyebrow">Why Winnow</p><h2>A large library is hard to keep track of.</h2></div><p>You don’t need another store. You need a better view of the games you already have. Winnow uses your play history and game updates to help you choose.</p></div>
        <StoryDemos />
      </section>

      <section className="workflow" id="how-it-works">
        <div className="shell">
          <div className="section-heading"><div><p className="eyebrow">How it works</p><h2>Start with the library you have.</h2></div><p>Keep your launchers. Winnow works alongside them, bringing their games and the history they record into one place.</p></div>
          <div className="workflow-layout">
            <ol className="workflow-steps">
              <li><span className="step-number">01</span><div><h3>Import your games</h3><p>Winnow reads the local files from your launchers. Optional Steam and Epic connections can add games that aren’t installed on this PC. Setup explains each connection, and you can skip it for later.</p></div></li>
              <li><span className="step-number">02</span><div><h3>See what’s worth another look</h3><p>The feed considers time played, time away, and updates since your last session. Every recommendation includes a reason. Postpone a game or mark it “Not interested” to shape future suggestions.</p></div></li>
              <li><span className="step-number">03</span><div><h3>Play, then pick up where you left off</h3><p>Launch through Winnow and your usual platform handles the game. Keep Winnow running to record sessions. Your play history, notes, and lists are there when you return.</p></div></li>
            </ol>
            <aside className="connection-notes"><h3>What stays local?</h3><p>Your library, play history, notes, and preferences live on your computer. There’s no Winnow account or cloud library to maintain.</p><h3>What uses the internet?</h3><p>Connected platforms, game details, cover art, and update checks use their respective providers. IGDB metadata is optional; Winnow works without it.</p><p className="read-only-note"><ShieldCheck size={20} aria-hidden="true" /> Winnow never edits your launchers’ files.</p><a className="text-link" href={sitePath('/docs/setup/')}>Read the setup guide <ArrowRight size={17} aria-hidden="true" /></a></aside>
          </div>
        </div>
      </section>

      <section className="collection-section shell">
        <div className="feature-copy"><p className="eyebrow">Organize your games</p><h2>One collection.<br />Your way of sorting it.</h2><p>Browse across platforms, filter down to what you feel like playing, and save a list for later.</p><dl className="feature-list"><div><dt>Collections that update themselves</dt><dd>Never played, Patched, Started, and Invested give you useful starting points. Live lists keep matching your saved filters.</dd></div><div><dt>Multiple copies, one game</dt><dd>Group store copies and editions together. Winnow asks you to review uncertain matches, and you can separate them again.</dd></div><div><dt>A record beyond hours played</dt><dd>See session history, read updates, and keep your own notes in the game journal.</dd></div></dl></div>
        <figure className="product-figure"><img src={sitePath('/assets/screenshots/desktop-merges.png')} width="1280" height="820" loading="lazy" alt="Winnow showing edition merge proposals for Prey and Deus Ex with Same game and Different games controls" /><figcaption>Review editions and store copies before grouping them.</figcaption></figure>
      </section>

      <section className="fullscreen-section">
        <div className="shell"><div className="section-heading"><div><p className="eyebrow">Desktop &amp; fullscreen</p><h2>Use a mouse.<br />Or grab a controller.</h2></div><div><p>Fullscreen has its own layout for browsing from a sofa or a handheld. It shares your library, lists, and preferences with the desktop view.</p><p>Adjust text size, screen margins, and interface scale to suit your display. Choose a theme and fonts that work for you.</p></div></div>
          <figure className="product-figure fullscreen-figure"><img src={sitePath('/assets/screenshots/fullscreen-library.jpg')} width="2560" height="1440" loading="lazy" alt="Winnow fullscreen library with Hades selected and controller navigation hints" /><figcaption>The same library, with navigation designed for a controller.</figcaption></figure>
        </div>
      </section>

      <section className="questions shell" aria-labelledby="questions-title">
        <div><p className="eyebrow">Before you install</p><h2 id="questions-title">A few practical details.</h2></div>
        <div className="question-list">
          <details><summary>Does Winnow replace Steam, Epic, or GOG?</summary><p>No. Your launchers still install and run your games. Winnow gives you a shared place to browse, organize, and decide what to play.</p></details>
          <details><summary>Will it find every game I own?</summary><p>It depends on what your platforms make available. Local files cover installed and cached games; optional connections can fill in more of your account library. GOG discovery uses local Galaxy data. You can also add games by hand.</p></details>
          <details><summary>What works on Linux?</summary><p>Local Steam discovery is the best starting point. Linux currently has more limited Epic and GOG discovery and no embedded platform sign-in. The <a href={sitePath('/docs/setup/#install')}>installation guide</a> covers packages and platform differences.</p></details>
          <details><summary>Is there a subscription or a paid version planned?</summary><p>No. Winnow is free and open source, with development supported by voluntary donations. There’s no plan for a paid version.</p></details>
          <details><summary>How can I help?</summary><p>Report a bug, suggest a useful improvement, or contribute code and documentation on <a href="https://github.com/safwyls/winnow">GitHub</a>. You don’t need to be a developer to give useful feedback.</p></details>
        </div>
      </section>

      <section className="closing shell">
        <p className="eyebrow">Ready when your library is</p><h2>There’s something good in there.</h2>
        <a className="button button-primary" href={releasesUrl}><Download size={18} aria-hidden="true" /> Download for Windows or Linux</a>
        <p>Free &amp; open source. Supported by voluntary donations.</p>
      </section>

      <footer><Brand /><p>A free, open-source library for your PC games.</p><div><a href={sitePath('/docs/')}>Docs</a><a href={sitePath('/developers/')}>Developers</a><a href="https://github.com/safwyls/winnow">GitHub</a></div></footer>
    </main>
  );
}

import type { Metadata } from 'next';
import { ArrowUpRight, BookOpen, SlidersHorizontal, Puzzle, Code2, ArrowRight } from 'lucide-react';
import DocsFrame from './DocsFrame';
import { articles } from './catalog';
import { sitePath } from '@/lib/site-path';

export const metadata: Metadata = { title: 'Documentation — Winnow', description: 'Set up your library, make Winnow your own, and build providers with the Winnow plugin SDK.' };
const icons = [BookOpen, SlidersHorizontal, Puzzle, Code2];

export default function DocsHome() {
  return <DocsFrame>
    <header className="docs-home-hero"><p className="eyebrow">The Winnow handbook</p><h1>Get to know<br />your library.</h1><p>From your first import to your first plugin. Practical guides for making Winnow work the way you play.</p><a className="button button-primary" href={sitePath('/docs/setup/')}>Set up Winnow <ArrowRight size={18} aria-hidden="true" /></a></header>
    <section className="docs-demo" aria-labelledby="demo-title">
      <p className="eyebrow">See it in action · 1 minute</p>
      <h2 id="demo-title">From a shelf to your next game.</h2>
      <p>A silent walkthrough of the desktop feed, previews, game details, library sorting, themes, and fullscreen shelves. Recorded in Winnow with a sample library.</p>
      <video controls playsInline preload="none" poster={sitePath('/docs/media/feed.webp')} width="1280" height="900" aria-label="One-minute Winnow walkthrough">
        <source src={sitePath('/docs/media/winnow-demo.mp4')} type="video/mp4" />
        <track kind="captions" src={sitePath('/docs/media/winnow-demo.vtt')} srcLang="en" label="English" />
        <a href={sitePath('/docs/media/winnow-demo.mp4')}>Download the walkthrough</a>
      </video>
      <details><summary>Walkthrough transcript</summary><ol>
        <li><strong>0:00</strong> Browse curated shelves in the desktop feed.</li>
        <li><strong>0:05</strong> Hover a cover for a preview and its actions.</li>
        <li><strong>0:12</strong> Open a game’s details, then explore its screenshots.</li>
        <li><strong>0:24</strong> Browse All games and change the library sort to Recently played.</li>
        <li><strong>0:41</strong> Find theme previews in Appearance settings.</li>
        <li><strong>0:46</strong> Browse games in fullscreen and move to another shelf.</li>
      </ol></details>
    </section>
    <div className="docs-guide-grid">{articles.map((article, index) => {
      const Icon = icons[index];
      return <a className="docs-guide-card" key={article.slug} href={sitePath(`/docs/${article.slug}/`)}><div><Icon size={23} aria-hidden="true" /><span>{article.audience === 'Players' ? 'Guide' : 'Reference + tutorial'}</span><ArrowUpRight size={18} aria-hidden="true" /></div><h2>{article.title}</h2><p>{article.description}</p><span className="docs-card-link">{article.audience === 'Players' ? 'Read the guide' : 'Explore the SDK'} <ArrowRight size={15} aria-hidden="true" /></span></a>;
    })}</div>
    <section className="docs-start-path"><div><p className="eyebrow">New here?</p><h2>A library that feels like yours.</h2></div><ol><li><strong>Bring your games together</strong><p>Start with local launcher discovery, then connect accounts for a fuller library.</p></li><li><strong>Find your next game</strong><p>Use curated shelves, filters, and lists to narrow the field.</p></li><li><strong>Make it comfortable</strong><p>Choose your theme, cover fit, library sort, and desktop or fullscreen view.</p></li></ol></section>
    <section className="docs-sdk-callout"><Code2 size={28} aria-hidden="true" /><div><p className="eyebrow">For plugin authors</p><h2>Bring a new source to Winnow.</h2><p>Build library, metadata, artwork, or recommendation providers. Start with a working example, then explore the contracts and host services.</p><a href={sitePath('/docs/plugin-sdk/')}>Build your first plugin <ArrowRight size={16} aria-hidden="true" /></a></div><span className="docs-api-tag">API 1</span></section>
  </DocsFrame>;
}

import type { Metadata } from 'next';
import { ArrowUpRight, BookOpen, SlidersHorizontal, Puzzle, Code2, ArrowRight } from 'lucide-react';
import DocsFrame from './DocsFrame';
import DocWalkthroughs from './DocWalkthroughs';
import { articles } from './catalog';
import { sitePath } from '@/lib/site-path';

export const metadata: Metadata = { title: 'Documentation — Winnow', description: 'Set up your library, make Winnow your own, and build providers with the Winnow plugin SDK.' };
const icons = [BookOpen, SlidersHorizontal, Puzzle, Code2];

export default function DocsHome() {
  return <DocsFrame>
    <header className="docs-home-hero"><p className="eyebrow">The Winnow handbook</p><h1>Get to know<br />your library.</h1><p>From your first import to your first plugin. Practical guides for making Winnow work the way you play.</p><a className="button button-primary" href={sitePath('/docs/setup/')}>Set up Winnow <ArrowRight size={18} aria-hidden="true" /></a></header>
    <DocWalkthroughs />
    <div className="docs-guide-grid">{articles.map((article, index) => {
      const Icon = icons[index];
      return <a className="docs-guide-card" key={article.slug} href={sitePath(`/docs/${article.slug}/`)}><div><Icon size={23} aria-hidden="true" /><span>{article.audience === 'Players' ? 'Guide' : 'Reference + tutorial'}</span><ArrowUpRight size={18} aria-hidden="true" /></div><h2>{article.title}</h2><p>{article.description}</p><span className="docs-card-link">{article.audience === 'Players' ? 'Read the guide' : 'Explore the SDK'} <ArrowRight size={15} aria-hidden="true" /></span></a>;
    })}</div>
    <section className="docs-start-path"><div><p className="eyebrow">New here?</p><h2>A library that feels like yours.</h2></div><ol><li><strong>Bring your games together</strong><p>Start with local launcher discovery, then connect accounts for a fuller library.</p></li><li><strong>Find your next game</strong><p>Use curated shelves, filters, and lists to narrow the field.</p></li><li><strong>Make it comfortable</strong><p>Choose your theme, cover fit, library sort, and desktop or fullscreen view.</p></li></ol></section>
    <section className="docs-sdk-callout"><Code2 size={28} aria-hidden="true" /><div><p className="eyebrow">For plugin authors</p><h2>Bring a new source to Winnow.</h2><p>Build library, metadata, artwork, or recommendation providers. Start with a working example, then explore the contracts and host services.</p><a href={sitePath('/docs/plugin-sdk/')}>Build your first plugin <ArrowRight size={16} aria-hidden="true" /></a></div><span className="docs-api-tag">API 1</span></section>
  </DocsFrame>;
}

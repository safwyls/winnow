'use client';

import { useState, useSyncExternalStore } from 'react';
import { Search, ArrowRight } from 'lucide-react';
import { sitePath } from '@/lib/site-path';

type Entry = { title: string; guide: string; text: string; href: string };
type Guide = { slug: string; title: string; audience: string; href: string };
const subscribe = () => () => {};
const clientReady = () => true;
const serverReady = () => false;

export default function DocsNavigation({ entries, guides, active }: { entries: Entry[]; guides: Guide[]; active?: string }) {
  const [query, setQuery] = useState('');
  const ready = useSyncExternalStore(subscribe, clientReady, serverReady);
  const terms = query.toLocaleLowerCase().trim().split(/\s+/).filter(Boolean);
  const results = terms.length ? entries.filter(entry => terms.every(term => `${entry.title} ${entry.guide} ${entry.text}`.toLocaleLowerCase().includes(term))) : [];
  return <aside className="docs-sidebar" aria-label="Documentation navigation">
    <label className="docs-search-label" htmlFor="docs-search">Search documentation</label>
    <div className="docs-search"><Search size={17} aria-hidden="true" /><input id="docs-search" type="search" value={query} onChange={event => setQuery(event.target.value)} disabled={!ready} placeholder={ready ? 'Find a setting or API…' : 'Loading search…'} autoComplete="off" /></div>
    <noscript><p>Search needs JavaScript. All guides are available in the navigation below.</p></noscript>
    {terms.length > 0 ? <div className="docs-results">
      <output className="docs-caption">{results.length} {results.length === 1 ? 'result' : 'results'}</output>
      {results.length === 0 && <p>No matches. Try “Steam”, “cache”, or “cover”.</p>}
      <ul>{results.map(entry => <li key={entry.href}><a href={entry.href} onClick={() => setQuery('')}><small>{entry.guide}</small><strong>{entry.title}</strong><span>{excerpt(entry.text, terms)}</span></a></li>)}</ul>
      <button className="docs-clear" onClick={() => setQuery('')}>Clear search</button>
    </div> : <nav aria-label="Guides">
      <a className="docs-overview-link" href={sitePath('/docs/')} aria-current={active ? undefined : 'page'}>Documentation home <ArrowRight size={14} aria-hidden="true" /></a>
      {['Players', 'Plugin authors'].map(audience => <div className="docs-nav-group" key={audience}>
        <p className="docs-caption">{audience === 'Players' ? 'Using Winnow' : 'Building for Winnow'}</p>
        <ul>{guides.filter(guide => guide.audience === audience).map(guide => <li key={guide.slug}><a href={guide.href} aria-current={active === guide.slug ? 'page' : undefined}>{guide.title}</a></li>)}</ul>
      </div>)}
      <div className="docs-support"><p>Something not covered?</p><a href="https://github.com/safwyls/winnow/issues">Ask on GitHub <ArrowRight size={14} aria-hidden="true" /></a></div>
    </nav>}
  </aside>;
}

function excerpt(text: string, terms: string[]) {
  const index = text.toLocaleLowerCase().indexOf(terms[0]);
  const start = Math.max(0, index - 36);
  return `${start ? '…' : ''}${text.slice(start, start + 130)}${text.length > start + 130 ? '…' : ''}`;
}

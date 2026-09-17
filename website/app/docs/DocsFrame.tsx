import type { ReactNode } from 'react';
import { sitePath } from '@/lib/site-path';
import DocsNavigation from './DocsNavigation';
import { guideLinks, searchEntries } from './catalog';

export default function DocsFrame({ active, children, toc }: { active?: string; children: ReactNode; toc?: ReactNode }) {
  return <div className="docs-page">
    <a className="docs-skip" href="#docs-content">Skip to content</a>
    <header className="site-header docs-header">
      <a className="brand" href={sitePath('/')} aria-label="Winnow home"><span className="brand-mark" aria-hidden="true" /><span>Winnow</span></a>
      <a className="docs-header-label" href={sitePath('/docs/')}>Documentation</a>
      <nav aria-label="Site navigation"><a href={sitePath('/plugins/')}>Plugins</a><a href={sitePath('/developers/')}>Developers</a><a href="https://github.com/safwyls/winnow">GitHub</a><a className="nav-cta" href="https://github.com/safwyls/winnow/releases">Download</a></nav>
    </header>
    <div className={`docs-layout shell ${toc ? '' : 'docs-layout-home'}`}>
      <DocsNavigation entries={searchEntries} guides={guideLinks} active={active} />
      <main id="docs-content" className="docs-main" tabIndex={-1}>{children}</main>
      {toc && <aside className="docs-toc" aria-label="On this page">{toc}</aside>}
    </div>
    <footer className="docs-footer"><a href={sitePath('/')}>Winnow</a><p>Your library. Your data. Your next game.</p><a href="https://github.com/safwyls/winnow/tree/main/docs">Documentation sources ↗</a></footer>
  </div>;
}

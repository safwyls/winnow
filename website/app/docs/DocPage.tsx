import { sitePath } from '@/lib/site-path';
import type { DocArticle } from './content-types';
import DocsFrame from './DocsFrame';
import { articles } from './catalog';

export default function DocPage({ article }: { article: DocArticle }) {
  const index = articles.findIndex(item => item.slug === article.slug);
  const next = articles[index + 1];
  const toc = <><p className="docs-caption">On this page</p><nav><ol>{article.sections.map(section => <li key={section.id}><a href={`#${section.id}`}>{section.title}</a></li>)}</ol></nav><a className="docs-back-top" href="#docs-content">Back to top ↑</a></>;
  return <DocsFrame active={article.slug} toc={toc}>
    <div className="docs-breadcrumb"><a href={sitePath('/docs/')}>Docs</a><span>/</span><span>{article.audience}</span></div>
    <header className="docs-article-header"><p className="eyebrow">{article.audience === 'Players' ? 'Winnow guides' : 'SDK 1 · .NET 10'}</p><h1>{article.title}</h1><p>{article.description}</p></header>
    <details className="docs-mobile-toc"><summary>On this page</summary><nav><ol>{article.sections.map(section => <li key={section.id}><a href={`#${section.id}`}>{section.title}</a></li>)}</ol></nav></details>
    <article className="docs-prose">{article.sections.map(section => <section id={section.id} key={section.id}><h2><a href={`#${section.id}`}>{section.title}<span className="docs-anchor" aria-hidden="true">#</span></a></h2>{section.content}</section>)}</article>
    <div className="docs-next">{next ? <a href={sitePath(`/docs/${next.slug}/`)}><small>Continue reading</small><strong>{next.title} →</strong></a> : <a href={sitePath('/docs/')}><small>Keep exploring</small><strong>Back to documentation →</strong></a>}</div>
  </DocsFrame>;
}

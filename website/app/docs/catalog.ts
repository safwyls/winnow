import { isValidElement, type ReactNode } from 'react';
import { setupArticle, configurationArticle, pluginsArticle } from './player-content';
import { sdkArticle } from './sdk-content';
import { sitePath } from '@/lib/site-path';

export const articles = [setupArticle, configurationArticle, pluginsArticle, sdkArticle];

function plainText(node: ReactNode): string {
  if (typeof node === 'string' || typeof node === 'number') return String(node);
  if (Array.isArray(node)) return node.map(plainText).join(' ');
  if (isValidElement<{ children?: ReactNode }>(node)) return plainText(node.props.children);
  return '';
}

export const searchEntries = articles.flatMap(article => [
  { title: article.title, guide: article.audience, text: article.description, href: sitePath(`/docs/${article.slug}/`) },
  ...article.sections.map(section => ({
    title: section.title, guide: article.title,
    text: plainText(section.content).replace(/\s+/g, ' ').trim(),
    href: sitePath(`/docs/${article.slug}/#${section.id}`),
  })),
]);

export const guideLinks = articles.map(({ slug, title, audience }) => ({ slug, title, audience, href: sitePath(`/docs/${slug}/`) }));

import type { Metadata } from 'next';
import DocPage from '../DocPage';
import { configurationArticle } from '../player-content';
export const metadata: Metadata = { title: 'Configure Winnow — Documentation', description: 'Configure appearance, library sorting, accounts, and fullscreen controls in Winnow.' };
export default function Page() { return <DocPage article={configurationArticle} />; }

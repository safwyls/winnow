import type { Metadata } from 'next';
import DocPage from '../DocPage';
import { pluginsArticle } from '../player-content';
export const metadata: Metadata = { title: 'Install plugins — Winnow Documentation', description: 'Install, enable, configure, update, and troubleshoot Winnow provider plugins.' };
export default function Page() { return <DocPage article={pluginsArticle} />; }

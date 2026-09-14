import type { Metadata } from 'next';
import DocPage from '../DocPage';
import { setupArticle } from '../player-content';
export const metadata: Metadata = { title: 'Set up Winnow — Documentation', description: 'Install Winnow, discover your local games, and connect your library.' };
export default function Page() { return <DocPage article={setupArticle} />; }

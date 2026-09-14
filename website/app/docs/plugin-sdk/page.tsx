import type { Metadata } from 'next';
import DocPage from '../DocPage';
import { sdkArticle } from '../sdk-content';
export const metadata: Metadata = { title: 'Plugin SDK — Winnow Documentation', description: 'Build Winnow provider plugins with .NET 10. API 1 contracts, manifest reference, host services, and a working example.' };
export default function Page() { return <DocPage article={sdkArticle} />; }

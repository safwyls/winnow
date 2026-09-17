import type { Metadata } from 'next';
import { sitePath } from '../lib/site-path';
import './globals.css';

export const metadata: Metadata = {
  icons: { icon: [
    { url: sitePath('/favicon.ico'), type: 'image/x-icon' },
    { url: sitePath('/favicon.svg'), type: 'image/svg+xml', sizes: 'any' },
  ] },
  title: 'Winnow — Find the game you meant to play',
  description: 'Bring your Steam, Epic, and GOG games into one library. Track play history, find unread updates, and choose what to play. Free and open source for Windows and Linux.',
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}

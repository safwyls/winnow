import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'Winnow — Find the game you meant to play',
  description: 'A local-first game library that surfaces the Steam, Epic, and GOG games you own, meant to play, and forgot existed.',
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}

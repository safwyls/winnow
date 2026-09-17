import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'Winnow — Find the game you meant to play',
  description: 'Bring your Steam, Epic, and GOG games into one library. Track play history, find unread updates, and choose what to play. Free and open source for Windows and Linux.',
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}

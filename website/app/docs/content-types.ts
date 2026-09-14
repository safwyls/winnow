import type { ReactNode } from 'react';

export type DocArticle = {
  slug: string;
  title: string;
  description: string;
  audience: 'Players' | 'Plugin authors';
  sections: { id: string; title: string; content: ReactNode }[];
};

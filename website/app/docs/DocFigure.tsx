import { sitePath } from '@/lib/site-path';
import Image from 'next/image';

export default function DocFigure({ name, alt, caption, fullscreen = false }: {
  name: string;
  alt: string;
  caption: string;
  fullscreen?: boolean;
}) {
  const src = sitePath(`/docs/media/${name}.webp`);
  return <figure className="docs-figure">
    <a href={src} aria-label={`Open full-size screenshot: ${alt}`}>
      <Image src={src} alt={alt} width={fullscreen ? 1920 : 1280} height={fullscreen ? 1080 : 820} loading="lazy" unoptimized />
    </a>
    <figcaption>{caption} <span>Open the image for a closer look.</span></figcaption>
  </figure>;
}

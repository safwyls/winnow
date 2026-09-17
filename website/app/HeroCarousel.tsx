'use client';

import { useEffect, useState } from 'react';
import { ChevronLeft, ChevronRight, Pause, Play } from 'lucide-react';
import { Carousel, CarouselContent, CarouselItem, type CarouselApi } from '@/components/ui/carousel';
import { sitePath } from '@/lib/site-path';

const screenshots = [
  ['desktop-library.png', 'Your library', 'Winnow desktop library with game covers and built-in collections'],
  ['desktop-merges.png', 'Edition and store matching', 'Winnow reviewing suggested merges for different game editions'],
  ['desktop-feed.png', 'Your recommendations', 'Winnow recommendations with a Hollow Knight preview and reasons to play'],
  ['desktop-details.png', 'Game details', 'Slay the Spire details, play history and screenshots in Winnow'],
  ['fullscreen-details.jpg', 'Fullscreen game details', 'Subnautica details and play history in fullscreen mode'],
  ['fullscreen-feed.jpg', 'Recently played in fullscreen', 'Fullscreen recommendations with Balatro and a recently played shelf'],
  ['fullscreen-library.jpg', 'Your library, fullscreen', 'Winnow fullscreen library showing game covers and controller navigation'],
] as const;

export default function HeroCarousel() {
  const [api, setApi] = useState<CarouselApi>();
  const [selected, setSelected] = useState(0);
  const [paused, setPaused] = useState(false);
  const [hovered, setHovered] = useState(false);
  const [focused, setFocused] = useState(false);
  const [hidden, setHidden] = useState(false);
  const [reducedMotion, setReducedMotion] = useState(true);

  useEffect(() => {
    const preference = window.matchMedia('(prefers-reduced-motion: reduce)');
    const updateMotion = () => setReducedMotion(preference.matches);
    const updateVisibility = () => setHidden(document.hidden);
    updateMotion(); updateVisibility();
    preference.addEventListener('change', updateMotion);
    document.addEventListener('visibilitychange', updateVisibility);
    return () => {
      preference.removeEventListener('change', updateMotion);
      document.removeEventListener('visibilitychange', updateVisibility);
    };
  }, []);

  useEffect(() => {
    if (!api) return;
    const update = () => setSelected(api.selectedScrollSnap());
    update();
    api.on('select', update);
    api.on('reInit', update);
    const stopOnDrag = () => setPaused(true);
    api.on('pointerDown', stopOnDrag);
    return () => {
      api.off('select', update); api.off('reInit', update); api.off('pointerDown', stopOnDrag);
    };
  }, [api]);

  const rotating = !paused && !hovered && !focused && !hidden && !reducedMotion;
  useEffect(() => {
    if (!api || !rotating) return;
    const timer = window.setInterval(() => api.scrollNext(), 5500);
    return () => window.clearInterval(timer);
  }, [api, rotating]);

  function choose(index: number) {
    setPaused(true);
    api?.scrollTo(index, reducedMotion);
  }

  return (
    <Carousel className="library-stage hero-carousel" opts={{ loop: true, duration: reducedMotion ? 0 : 25 }} setApi={setApi}
      aria-label="Winnow app screenshots"
      onMouseEnter={() => setHovered(true)} onMouseLeave={() => setHovered(false)}
      onFocusCapture={() => setFocused(true)}
      onBlurCapture={(event) => { if (!event.currentTarget.contains(event.relatedTarget)) setFocused(false); }}>
      <CarouselContent className="hero-slides" aria-live={rotating ? 'off' : 'polite'}>
        {screenshots.map(([file, title, alt], index) => (
          <CarouselItem className="hero-slide" key={file} aria-label={`${index + 1} of ${screenshots.length}: ${title}`} aria-hidden={index !== selected}>
            <img src={sitePath(`/assets/screenshots/${file}`)} alt={alt} width={file.endsWith('.jpg') ? 2560 : 1280}
              height={file.endsWith('.jpg') ? 1440 : 820} fetchPriority={index === 0 ? 'high' : 'auto'} decoding="async" />
          </CarouselItem>
        ))}
      </CarouselContent>
      <div className="hero-carousel-controls">
        <p className="hero-slide-caption">{screenshots[selected][1]} <span>{selected + 1} / {screenshots.length}</span></p>
        <div className="hero-carousel-dots" aria-label="Choose screenshot">
          {screenshots.map(([, title], index) => (
            <button key={title} type="button" aria-label={`Show ${title}`} aria-current={selected === index ? 'true' : undefined}
              onClick={() => choose(index)}><span /></button>
          ))}
        </div>
        <div className="hero-carousel-actions">
          <button type="button" aria-label="Previous screenshot" onClick={() => choose((selected + screenshots.length - 1) % screenshots.length)}><ChevronLeft size={18} aria-hidden="true" /></button>
          {!reducedMotion && <button type="button" aria-label={paused ? 'Resume slideshow' : 'Pause slideshow'} onClick={() => setPaused(!paused)}>
            {paused ? <Play size={16} aria-hidden="true" /> : <Pause size={16} aria-hidden="true" />}
          </button>}
          <button type="button" aria-label="Next screenshot" onClick={() => choose((selected + 1) % screenshots.length)}><ChevronRight size={18} aria-hidden="true" /></button>
        </div>
      </div>
    </Carousel>
  );
}

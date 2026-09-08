'use client';

import { useState, type CSSProperties } from 'react';
import { Download, List } from 'lucide-react';

type DemoGame = {
  id: number;
  title: string;
  cover: string;
  idle: string;
  playtime: string;
  saturation: number;
  brightness: number;
};

const games: DemoGame[] = [
  { id: 108710, title: 'Alan Wake', cover: 'https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/108710/library_600x900.jpg', idle: 'idle 4y 6mo', playtime: '6h', saturation: 0.22, brightness: 0.68 },
  { id: 1145360, title: 'Hades', cover: 'https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/1145360/library_600x900.jpg', idle: 'idle 2y 1mo', playtime: '28h', saturation: 0.34, brightness: 0.74 },
  { id: 1244090, title: 'Sea of Stars', cover: 'https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/1244090/library_600x900.jpg', idle: 'idle 11mo', playtime: '18h', saturation: 0.54, brightness: 0.85 },
];

const tileStyle = (game: DemoGame) => ({
  '--idle-saturation': game.saturation,
  '--idle-brightness': game.brightness,
} as CSSProperties);

function TileFace({ game }: { game: DemoGame }) {
  return (
    <>
      <img src={game.cover} alt="" />
      <span className="details-fold" aria-hidden="true"><List size={13} /></span>
      <span className="app-tile-overlay">
        <strong>{game.title}</strong>
        <span className="app-tile-meta">{game.playtime} · {game.idle}</span>
        <span className="app-tile-actions"><i>Steam</i><b><Download size={18} strokeWidth={3} aria-hidden="true" /></b></span>
      </span>
    </>
  );
}

export default function StoryDemos() {
  const [awakeId, setAwakeId] = useState<number | null>(null);
  const [patchOpen, setPatchOpen] = useState(false);
  const [reasonOpen, setReasonOpen] = useState(false);
  const seaOfStars = games[2];
  const hades = games[1];

  return (
    <div className="story-grid">
      <article className="story-card">
        <div className="tile-demo dormancy-demo" aria-label="Three dormant games at different stages of fading">
          {games.map((game) => (
            <button key={game.id} type="button" className={`demo-tile${awakeId === game.id ? ' awake' : ''}`} style={tileStyle(game)} aria-pressed={awakeId === game.id} aria-label={`${game.title}, ${game.idle}. Wake the cover.`} onClick={() => setAwakeId(awakeId === game.id ? null : game.id)}>
              <TileFace game={game} />
            </button>
          ))}
        </div>
        <p className="demo-hint">Hover, focus, or tap a cover</p>
        <h3>See what went quiet</h3>
        <p>Cover art fades as a game sits dormant. Hover it and the colour wakes up—an old intention, made visible again.</p>
      </article>

      <article className="story-card">
        <div className="tile-demo single-tile-demo">
          <button type="button" className={`demo-tile feature-tile patch-demo${patchOpen ? ' awake open' : ''}`} style={tileStyle(seaOfStars)} aria-pressed={patchOpen} aria-label="Sea of Stars, 18.2 hours played, patched since you played. Show the unread update." onClick={() => setPatchOpen(!patchOpen)}>
            <TileFace game={seaOfStars} />
            <i className="game-patch" aria-hidden="true" />
          </button>
          <div className={`demo-response${patchOpen ? ' open' : ''}`} aria-live="polite">
            <span className="patch-dot" />
            <p><strong>Update since your last session</strong><small>18.2h played · Steam</small></p>
          </div>
        </div>
        <p className="demo-hint">Select the unread dot</p>
        <h3>Notice what changed</h3>
        <p>A pink unread dot marks a major update since your last session. Open it to read the patch notes you missed.</p>
      </article>

      <article className="story-card">
        <div className="tile-demo single-tile-demo reason-demo">
          <button type="button" className={`demo-tile feature-tile${reasonOpen ? ' awake open' : ''}`} style={tileStyle(hades)} aria-pressed={reasonOpen} aria-label="Hades, 28.6 hours played, last played two years ago. Show why Winnow surfaced it." onClick={() => setReasonOpen(!reasonOpen)}>
            <TileFace game={hades} />
          </button>
          <div className={`demo-response reason-response${reasonOpen ? ' open' : ''}`} aria-live="polite">
            <span>“</span>
            <p><strong>You were invested, then the trail went cold.</strong><small>28.6h played · 2 years idle</small></p>
          </div>
        </div>
        <p className="demo-hint">Ask why it surfaced</p>
        <h3>Know why it surfaced</h3>
        <p>Each recommendation names the signals behind it: your playtime, your gap, and what happened while you were away.</p>
      </article>
    </div>
  );
}

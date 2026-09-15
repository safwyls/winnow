import { sitePath } from '@/lib/site-path';

const walkthroughs = [
  {
    "id": "desktop",
    "title": "Desktop walkthrough",
    "duration": "1 min 44 sec",
    "description": "Explore the feed, hover previews, details, library sorting, and preferences.",
    "poster": "feed.webp",
    "width": 1280,
    "height": 900,
    "chapters": [
      {
        "time": "0:00",
        "text": "Browse curated shelves in the desktop feed."
      },
      {
        "time": "0:12",
        "text": "Hover a cover for a game preview."
      },
      {
        "time": "0:24",
        "text": "Open details and explore your play history."
      },
      {
        "time": "0:42",
        "text": "Browse the screenshot gallery."
      },
      {
        "time": "0:50",
        "text": "Choose a library sort."
      },
      {
        "time": "1:10",
        "text": "Return to recently played games."
      },
      {
        "time": "1:22",
        "text": "Compare themes in Appearance settings."
      },
      {
        "time": "1:32",
        "text": "Find your preferred default library sort in settings."
      }
    ]
  },
  {
    "id": "fullscreen",
    "title": "Fullscreen walkthrough",
    "duration": "1 min 53 sec",
    "description": "Browse shelves, read game details, explore screenshots, and adjust text size.",
    "poster": "fullscreen-poster.webp",
    "width": 1920,
    "height": 1160,
    "chapters": [
      {
        "time": "0:00",
        "text": "Browse games across a fullscreen shelf."
      },
      {
        "time": "0:25",
        "text": "Move vertically between curated shelves."
      },
      {
        "time": "0:41",
        "text": "Open the overview and read your play history."
      },
      {
        "time": "1:05",
        "text": "Explore the screenshot gallery."
      },
      {
        "time": "1:33",
        "text": "Adjust text size in Appearance for comfortable reading."
      }
    ]
  }
];

export default function DocWalkthroughs() {
  return walkthroughs.map((walkthrough) => <section key={walkthrough.id} className="docs-demo" aria-labelledby={`${walkthrough.id}-walkthrough`}>
    <p className="eyebrow">See it in action · {walkthrough.duration}</p>
    <h2 id={`${walkthrough.id}-walkthrough`}>{walkthrough.title}</h2>
    <p>{walkthrough.description} A silent recording of Winnow using a sample library, with time to read each screen.</p>
    <video controls playsInline preload="none" poster={sitePath(`/docs/media/${walkthrough.poster}`)} width={walkthrough.width} height={walkthrough.height} aria-label={walkthrough.title}>
      <source src={sitePath(`/docs/media/winnow-${walkthrough.id}.mp4`)} type="video/mp4" />
      <track kind="captions" src={sitePath(`/docs/media/winnow-${walkthrough.id}.vtt`)} srcLang="en" label="English" />
      <a href={sitePath(`/docs/media/winnow-${walkthrough.id}.mp4`)}>Download the {walkthrough.id} walkthrough</a>
    </video>
    <details><summary>Walkthrough transcript</summary><ol>{walkthrough.chapters.map((chapter) => <li key={chapter.time}><strong>{chapter.time}</strong> {chapter.text}</li>)}</ol></details>
  </section>);
}

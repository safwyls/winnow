# Documentation media capture — 14 September 2026

Recorded the running Windows app at source commit `929f3d5`, using an isolated
sample library. The documentation homepage includes separate silent desktop (1:44) and fullscreen (1:53) walkthroughs;
setup, configuration, and plugin installation include eight screenshots. Media lives
in `website/public/docs/media/`.

## Capture setup

- Built Debug to `C:\Temp\winnow-docs-demo-build`, with no application code changes.
- Started `Winnow.exe --data-dir C:\Temp\winnow-docs-demo-data --no-sync`.
  The initial launch also used `--seed-sample`.
- Used the Computer Use Windows API to operate the actual window: hover previews,
  details, screenshot gallery, library sorting, settings, and F11 fullscreen.
- Captured desktop at its normal 1280 × 820 size. Fullscreen used the centered
  2560 × 1440 canvas on a 3440 × 1440 display, excluding the side gutters.
- Used ShareX's installed ffmpeg. Window-title GDI capture returned black frames
  with the accelerated app; bounded desktop capture worked. The experimental
  Graphics Capture input returned no frames. Neither failed capture was published.

Example desktop recording command (coordinates must match the current window):

```powershell
ffmpeg -f gdigrab -framerate 30 -draw_mouse 0 `
  -offset_x 156 -offset_y 156 -video_size 1280x820 -i desktop `
  -t 240 -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p desktop-raw.mp4
```

The original video retained the pointer. Stills were recaptured with `-draw_mouse 0`
to avoid GDI's black square around the app's custom pointer. The app-drawn pointer
can remain visible. For a still, replace encoding options with `-frames:v 1 image.png`.
Fullscreen capture uses offsets 440/0, size 2560x1440, and `-vf scale=1920:1080`.

The desktop edit reuses the original raw footage. Fullscreen was recorded again
with longer pauses and the sample library's Rosé Pine theme. Neither edit speeds up
navigation. These are the raw segments retained in playback order:

| Source | Start (seconds) | Duration | Subject |
| --- | ---: | ---: | --- |
| Desktop | 0 | 12 | Feed |
| Desktop | 75 | 12 | Hover preview |
| Desktop | 24 | 18 | Details |
| Desktop | 57 | 8 | Screenshot gallery |
| Desktop | 94 | 20 | Library sort menu |
| Desktop | 114 | 12 | Recently played |
| Desktop | 148 | 10 | Appearance |
| Desktop | 160 | 12 | Default library sort settings |
| Fullscreen, new main take | 0 | 25 | Horizontal browsing |
| Fullscreen, new main take | 30 | 16 | Change shelf |
| Fullscreen, new main take | 58 | 24 | Overview and history |
| Fullscreen, new main take | 120 | 28 | Screenshot gallery |
| Fullscreen, Appearance take | 0 | 20 | Text size |

Desktop is 1280 × 900 and fullscreen is 1920 × 1160, including a dark chapter-label
footer below the uncropped app canvas. Both use H.264/yuv420p at 30 fps, CRF 21,
and `-movflags +faststart`, with no audio. Each has its own WebVTT chapter captions,
HTML transcript, poster, and video controls. Stills use WebP quality 88 and retain
their captured resolution; the docs offer full-size links and lazy loading.

An additional fullscreen Library take had missing cover artwork and was excluded.
The fullscreen walkthrough instead ends with Appearance. Text size was restored to
100% after recording. The missing artwork's cause was not investigated in this media task.

## Sample data and limits

The seed library was reduced to 39 titles with verified Steam app IDs. Public Steam
store descriptions, cover artwork, hero images, and screenshots were cached before
recording. Play history, ownership, and installation flags are demonstration data;
they do not establish actual ownership or play activity. Game artwork remains the
property of its respective owners. No game was launched or installed.

The demo cache used the existing IGDB screenshot slot for Steam-sourced screenshot
files. Consequently the details view's gallery source label says IGDB; this capture
does not demonstrate a live IGDB connection. Do not use that label as provider
provenance. The walkthrough demonstrates browsing and gallery interaction only.

No account authentication was recorded. An externally configured Steam key was
detected by the development build, but its value was neither displayed nor copied
into the fixture. Platform credential screens are excluded from the output.
Automatic updates were disabled in the isolated copy; the guide explains that
released builds enable them by default. This source build contains no packaged
providers, and the plugin screenshot caption distinguishes it from a release.
Fullscreen was driven with keyboard input; controller hardware was disconnected.

## Verification

- Application Debug build succeeded with no warnings or errors.
- Inspected the app after each interaction and reviewed screenshot contact sheets.
- Reviewed video chapter frames and adjusted the edit boundaries to include preview,
  sort selection, and vertical shelf transition.
- ffmpeg reports 104.00 seconds for desktop and 112.97 seconds for fullscreen,
  both 30 fps H.264/yuv420p. Both finished files decode without errors.
- Static Pages build verifies screenshot, video, poster, and caption URLs. The
  verification now includes `video`, `source`, and `track` elements.
- Scoped documentation lint passes. The full website lint remains blocked by
  pre-existing errors in `components/ui` (including carousel, chart, and form wrappers).

Raw footage and the temporary sample library stay outside the repository. This
change does not publish the site or change desktop/fullscreen application behavior.

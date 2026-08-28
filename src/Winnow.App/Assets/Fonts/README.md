# Bundled fonts

tokens.axaml resolves all three families from this folder as AvaloniaResource.
There is no system-font fallback — the display face is load-bearing (§3).

**Static instances, deliberately.** Avalonia 11.x has no API for variable-font
axes: `FontFeatures` maps to HarfBuzz OpenType *features*, not `fvar` axes, so a
variable TTF renders at its default (light) instance and every `FontWeight="Bold"`
display style came out wrong. The static cuts below are what the design scale
actually asks for.

| File | Family / weight | Used by |
|---|---|---|
| `BricolageGrotesque-Bold.ttf` | Bricolage Grotesque 700 | Display L, Display S, tile titles |
| `PlusJakartaSans-Regular.ttf` | Plus Jakarta Sans 400 | Body |
| `PlusJakartaSans-Medium.ttf` | Plus Jakarta Sans 500 | Body L |
| `PlusJakartaSans-SemiBold.ttf` | Plus Jakarta Sans 600 | Label |
| `IBMPlexMono-Regular.ttf` | IBM Plex Mono 400 | Data, Data S |
| `IBMPlexMono-Medium.ttf` | IBM Plex Mono 500 | Data emphasis |

§3's `wdth` 105–110 is unreachable twice over: Avalonia can't set axes, and
Bricolage's own `wdth` axis tops out at 100. The static Bold is wdth 100 — the
widest cut the face has, and what the mock's clamped `wdth 105` renders anyway.

## Sources & licence

All three are SIL Open Font License 1.1.

- Bricolage Grotesque — Atelier Triay, <https://github.com/ateliertriay/bricolage>
- Plus Jakarta Sans — Tokotype, <https://github.com/tokotype/PlusJakartaSans>
- IBM Plex Mono — IBM, <https://github.com/IBM/plex>

# Bundled fonts

[tokens.axaml](../../Themes/tokens.axaml) resolves all three families from this folder as AvaloniaResource.
There is no system-font fallback â€” the display face is part of the visual identity.

The app bundles the static weights its typography uses. Display typography uses Bricolage
Bold at its native width; no variable-font axis settings are required.

| File | Family / weight | Used by |
|---|---|---|
| `BricolageGrotesque-Bold.ttf` | Bricolage Grotesque 700 | Display L, Display S, tile titles |
| `PlusJakartaSans-Regular.ttf` | Plus Jakarta Sans 400 | Body |
| `PlusJakartaSans-Medium.ttf` | Plus Jakarta Sans 500 | Body L |
| `PlusJakartaSans-SemiBold.ttf` | Plus Jakarta Sans 600 | Label |
| `IBMPlexMono-Regular.ttf` | IBM Plex Mono 400 | Data, Data S |
| `IBMPlexMono-Medium.ttf` | IBM Plex Mono 500 | Data emphasis |

## Sources & licence

All three are SIL Open Font License 1.1.

- Bricolage Grotesque â€” Atelier Triay, <https://github.com/ateliertriay/bricolage>
- Plus Jakarta Sans â€” Tokotype, <https://github.com/tokotype/PlusJakartaSans>
- IBM Plex Mono â€” IBM, <https://github.com/IBM/plex>

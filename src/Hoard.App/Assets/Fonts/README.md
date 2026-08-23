# Bundled fonts (placeholder)

tokens.axaml expects these font families to be bundled here as
AvaloniaResource (all SIL OFL):

- Bricolage Grotesque (`DisplayFont`)
- Plus Jakarta Sans (`BodyFont`)
- IBM Plex Mono (`DataFont`)

Downloading and adding the actual `.ttf` files is handled by another agent.
Until they land, Avalonia falls back to the default typeface; the app must
still build and run without them.

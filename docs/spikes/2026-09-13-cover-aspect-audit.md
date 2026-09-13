# Portrait cover source audit

On 13 September 2026, investigated fullscreen cover padding using image headers in
the local Winnow cache, read-only SQLite metadata, source inspection, and public CDN
requests. No application data or cached artwork was changed.

## Findings

Winnow's Steam capsule source tries only the legacy app-ID paths ending in
`library_600x900_2x.jpg` and `library_600x900.jpg`. Both can return 404 even when
Steam has a portrait at a hashed path. Steam appinfo's
`common.library_assets_full.library_capsule` identifies those paths.

The following cached Steam-keyed covers were 528×704. Fetching the English image2x
path advertised in appinfo returned a valid 600×900 image for each:

| Game | App ID | Published asset path beneath store_item_assets/steam/apps/{id}/ |
|---|---|---|
| RuneScape: Dragonwilds | 1374490 | f72e5a29377820dd3d1d76e0c2eb5a8fcfa34c7d/library_capsule_2x.jpg |
| RV There Yet? | 3949040 | 701632e4a84f8378c23fdf18ae127cb6a0f3f904/library_capsule_2x.jpg |
| How to Fish | 4001890 | 280684ffc5f512083f4e81a551df2b9ae3ff71db/library_capsule_2x.jpg |
| STAR WARS Republic Commando | 6000 | 30259337ac341e9fbe5410650e7aabf1b63796b6/library_600x900_2x.jpg |

Appinfo for PixelJunk Eden (105800), Far Cry (13520), Tribes: Ascend (17080), and
LUMBERMANCER (491210) contained no library capsule path. Legacy JPG and PNG portrait
requests returned 404 on the sampled Akamai endpoint. The configured Cloudflare
endpoint also returned 404 for both legacy JPG names for 1374490, 3949040, 13520,
and 6000. Absence in these probes does not prove that no suitable artwork exists
at another provider.

The local cache contained 910 Steam-keyed source images: 768 were 600×900 and
141 differed from 2:3 by more than 0.025 in width/height ratio. Of 305 IGDB-keyed
source images, 297 exceeded that threshold. These are cached assets, not counts
of currently displayed games; caches can overlap and retain old choices.

Steam keys do not establish image provenance: the pipeline saves IGDB fallback
bytes under the original Steam key. The dominant fallback size was 528×704.
IGDB's cover_big_2x is a fit within a maximum size, preserving the input ratio,
not a request for a 2:3 composition. Some cached originals are square or landscape.

Successful cache files are read before providers and have no freshness check.
Consequently, correcting URL resolution alone will not upgrade cached fallback
images. Their actual source is not recorded separately from their request key.

Fullscreen fits the whole image using Uniform and fills the remaining space with
edge colors. Desktop crops using UniformToFill. The same provider gap therefore
looks different on the two surfaces. Separately, fullscreen reserves a 3px border
inside its 2:3 outer frame, making the inner image area slightly narrower than
2:3; even an exact portrait can show a very thin padding strip.

## Potential remedy

Resolve published Steam portrait paths from appinfo, with existing legacy URLs as
a compatibility path. Record which source answered and support a bounded refresh
of cached fallbacks so newly available Steam art can replace them. Preserve manual
artwork and explicit IGDB pins. Keep padding for genuinely nonstandard originals;
increasing IGDB resolution does not change their composition. Assess desktop and
fullscreen together because they share cover selection and cache bytes.

This investigation does not implement those changes or claim that all 141 cached
Steam mismatches are recoverable. Four of the eight deliberately selected samples
had retrievable standard portraits; this sample is not a population estimate.

## Reproduction and sources

- Read source image headers with Pillow; exclude hero, screenshot, and user keys.
- Join Steam external IDs through releases to works using SQLite mode=ro; do not
  read credentials or account fields.
- Read public appinfo through https://api.steamcmd.net/v1/info/{appid}, which is an
  existing project dependency but a third-party mirror of Steam metadata.
- Verify advertised image bytes directly from Valve's shared.fastly.steamstatic.com
  CDN in memory; inspect actual dimensions rather than trusting filenames.
- [Steam library asset specification](https://partner.steamgames.com/doc/store/assets/libraryassets?language=english)
  defines the library portrait as 600×900.
- [IGDB image specification](https://api-docs.igdb.com/#images) defines cover_big as
  a maximum 264×374 Fit rendition, with doubled limits for _2x.
- Implementation: SteamCapsuleSource, IgdbCoverSource, CoverPipeline,
  FullscreenCoverPadding, FullscreenCover in FullscreenBrowsePage, and GameTileView.

# Artwork browser parity

The Electron browser separates preview from committing artwork. Hero, Cover and Icon
retain their selected candidate, source and scroll position. Providers load, fail,
retry and page independently; unsupported source/slot combinations remain explicit.
Closing cancels named requests, and reopening reads Current again. Writes carry the
observed revision, refresh library projections before Current, and distinguish a
completed write from a later refresh failure. An uncertain write is never retried
automatically.

The desktop overlay retains the original 24px inset and 1440px width limit. Preview
and actions stay fixed while the gallery scrolls. Hero crops preserve the underlying
desktop ratio or 16:9; Cover uses the saved fit/fill preference in a 2:3 frame; Icon
includes the 32px sample and light/dark transparency previews. Fullscreen has its own
page layout, trigger slot navigation and scaled controls. Back/Escape restores the
invoking control; metadata drafts survive opening the artwork browser.

The main process owns the OS file picker and validates its selected file before
posting at most 16 MiB to the authenticated image endpoint. Renderer-supplied paths
are ignored. The API decodes the image and checks the selected slot's revision.

On 2026-09-29, all 41 focused tests passed: 26 component cases, 12 file/transport
boundary cases and three real-backend imports. The live tests use a generated BMP
pixel, verify PNG decoding, invalid-image refusal, stale-revision refusal, reset and
unchanged other slots. They create and remove only their own manual entries in a
throwaway backend.

All six native artwork cases passed. They cover the three original desktop sizes
(1280×820, 1200×640 and 1920×1080), both directions of modal focus traversal, actual
image IPC and simulated controller sampling at default/140% text with reduced
motion. The short-window test exposed an icon preview collapsing to zero height;
removing inherited form spacing and fitting crop frames to their available region
fixed it. Earlier fixture failures came from reusing the already-saved candidate and
pressing Back before projection refresh completed; the assertions now isolate state
and wait for the operation to finish.

Evidence: `.tmp/artwork-browser-live.log`, `.tmp/artwork-details-native-final.log`
and captures under `.tmp/electron-artwork-details-final`. Images are deterministic
provider fixtures; these checks do not contact Steam/IGDB or establish physical
controller compatibility. Thirteen of the fifteen original ArtworkBrowserTests
methods are mapped. Desktop metadata overlay geometry, fullscreen metadata field
navigation and a controller-driven fullscreen file browser remain outstanding.

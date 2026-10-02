# Details geometry and persistent refresh feedback

Desktop Details now uses the original 0.667 height fraction and 410px About measure,
including the empty-description placeholder. Installation paths and external identifiers
remain visible directly in Library. More uses the specified Edit details label and preserves
the source order of the five maintenance actions on both surfaces.

The Details view owns metadata refresh independently of the transient More menu. Invoking
it closes More, restores focus, and displays a persistent footer outside the reading area.
Pending and completed feedback update the same polite text node, without an accessible-name
override. Reopening More while pending disables the action. A successful refresh preserves
the selected tab; a refusal remains amber. Fullscreen uses its larger text scale and aligns
the footer with the reading area.

## Verification

- Build and TypeScript check pass: `.tmp/details-structure-build-final.log`.
- All 3,201 component/live API cases pass in 158 files without skips, in 49.74s:
  `.tmp/details-structure-integration-final.log`.
- All 35 affected native cases passed in 2.3 minutes before the final fullscreen typography
  adjustment. They cover Details, metadata editing, action overlays and real library/merge
  workflows: `.tmp/details-structure-native.log` and
  `.tmp/details-structure-native-first/results.json`.
- All 15 final native Details cases pass in 50.2s, including the adjusted footer type and
  independently resolved muted/amber colors: `.tmp/details-structure-native-final.log` and
  `.tmp/details-structure-native-verified/results.json`.
- Native geometry uses every original width fixture, from 1200 through 7680, and every
  height fixture, from 640 through 2160. The measured cap is further bounded by the 40px
  outside margins when the viewport is smaller. Populated and absent About prose retain
  the same measure and primary ink.
- A held API transport response proves the footer remains through menu reconstruction and
  reading-area scrolling in both modes. Completion retains Library selection and More focus.
- Desktop and fullscreen refusal captures were inspected. This caught desktop-sized text
  in the first fullscreen footer, which was corrected and verified in the final native run.
- Initial component failures were outdated disclosure/copy expectations. The first full
  suite then exposed a new reception-order assertion applied to fullscreen; that assertion
  belongs to desktop. Fullscreen retains its specified dedicated About reading page. The
  corrected full suite passes, without changing that presentation.

Seven original methods now have behavioral evidence. Three assertions on invalid CLR
converter inputs or ConvertBack are classified individually as framework-specific. Other
Details structure and fullscreen/controller contracts remain open: the new geometry and
footer checks do not establish complete coverage of those larger contracts.

The inventory is 1,199 ported, 625 retained backend, 23 framework-specific, 487 pending and
101 partial methods out of 2,435. Complete migration and the final combined native run
remain open. No .NET source changed.

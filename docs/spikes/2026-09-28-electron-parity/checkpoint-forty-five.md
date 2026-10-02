# Dedicated desktop Merges navigation

Desktop Merges now has a direct rail destination with the original tooltip, a plain label
and full opacity even when its queue is empty. It opens the shared identity-review screen
without Library-tools tabs. Details closes back to the originating member and Escape
returns to Library. Fullscreen keeps its existing Library-tools route and controller sheets.

The desktop queue responds to Up/Down, Space, S/Enter and D. The host handles Escape.
Home/End and unrelated keys remain with their control; modified shortcuts cannot write
a decision. Fullscreen sheets retain their own Home/End navigation. Theme API 1 now has
an optional `Merges` renderer and the `merges` page, with the shared host screen as fallback.

## Verification

- Build and TypeScript check pass: `.tmp/merge-navigation-build.log`.
- All 3,184 component/live API cases pass across 157 files, without skips, in 50.58s:
  `.tmp/merge-navigation-integration.log`.
- All 35 native cases pass in 2.4 minutes, including all 22 Avalon layout cases, seven
  production merge cases and six surface probes. The backend executable is explicitly
  configured. Evidence: `.tmp/merge-navigation-native.log` and
  `.tmp/merge-navigation-native-verified/results.json`.
- Native routing verifies the rail's name, tooltip, lack of count and computed opacity;
  opens real game Details; restores the same row cursor; and returns to Library with
  unchanged candidates and history. The route capture was inspected.
- Component tests cover empty-queue rail identity, independent fullscreen navigation,
  unrelated/modified keys and focused control isolation. Each S, uppercase S, Enter, D
  and uppercase D dispatches exactly one answer for the selected card and header.
- The initial focused run passed 216 of 217 cases. Its new app assertion ran during the
  fullscreen preparation overlay; using the existing readiness helper fixes it. All 51
  app cases pass on rerun: `.tmp/merge-navigation-app-final.log`.

Three additional `MergesSurfaceTests` methods gain complete Electron evidence. The original
sort menu and centralized screen-copy guard remain pending. Older migration descriptions
now describe Up/Down clamping accurately instead of the removed desktop Home/End extension.

The inventory is 1,178 ported, 625 retained backend, 20 framework-specific, 511 pending and
101 partial methods, out of 2,435. The migration gate remains incomplete. No .NET source
changed; the complete Release evidence from checkpoint 39 remains applicable.

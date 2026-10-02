---
id: TASK-336
title: Open all web links inside Winnow when selected
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 17:43'
updated_date: '2026-09-17 17:48'
labels: []
dependencies: []
ordinal: 378000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The In Winnow preference only opens Steam patch notes, so store pages unexpectedly leave the app. Expand embedded browsing to web links with store-client routing and browser fallback.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 In Winnow opens HTTP and HTTPS links including store pages and other websites; redirects and page links remain embedded.
- [x] #2 Store client sends supported store pages to the client and other web links to the system browser; native game actions retain their targets.
- [x] #3 Embedded browsing stays isolated from sign-in capture and host bridges; non-web navigation, downloads and permissions retain explicit controls.
- [x] #4 Desktop and fullscreen routing and failure behavior are tested and current docs and settings copy updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Expand the isolated reader policy and WebView window; remove the router news-only gate and audit link entry points; test policy/router and desktop/fullscreen behavior and update documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Expanded the isolated reader from a Steam-news origin/path allowlist to HTTP/HTTPS entry, redirects, popups and frames. Non-web page navigation is blocked; the initial blank native view precedes handlers. In-private profile, no host objects/messages/injected scripts, denied permissions/downloads and blocked external schemes remain. General browser title, address and Back/Forward controls replace news-only presentation. Router sends web pages to the reader for In Winnow; supported canonical Steam store pages use Steam for Store client and other web pages use the system browser. Desktop artwork sources, release notes, provider websites and setup/help links now honor the preference. Explicit sign-in challenge handoffs, manual update downloads and opaque native plugin actions remain native/system-browser routes. Updated visual and architectural docs and settings copy. Verification: solution build zero warnings/errors; 155 policy/router/settings/auth unit tests and 73 desktop/fullscreen headless interaction tests passed. Tests include non-Steam sites, HTTP, store-client fallbacks, native-action preservation and keyboard artwork-source routing. Live external websites in native WebView were not smoke-tested; native hardening was source-reviewed and unchanged.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
In Winnow now opens general web and store pages, with browser history controls; Store client retains browser fallback for non-store links. Wired artwork and settings web links through the shared preference. Verified with 228 focused tests and a warning-free solution build.
<!-- SECTION:FINAL_SUMMARY:END -->

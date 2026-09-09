# Fullscreen design review

These are image concepts for a separate TV-distance, gamepad-first presentation path.
They do not implement the UI. The owning visual proposal is in `design-system.md` §8;
the presentation boundary is in `game-library-design.md` §5.1. Desktop and fullscreen
maintenance expectations are in `AGENTS.md`.

| Concept | File |
|---|---|
| Home / For you | [Home](home.png) |
| Game details | [Game details](game-details.png) |
| Library | [Library](library.png) |
| Activity | [Activity](activity.png) |
| Settings / Appearance | [Settings](settings.png) |

The user approved the five-screen direction and authorized implementation. Artwork, counts,
sessions, notes and battery status are illustrative; these images
are not measurements or screenshots of the application. Real game artwork will come from
the existing cover and metadata services, not these generated illustrations.

Generated with the built-in image-generation tool. The home image was the visual reference
for the later screens. Additional-screen prompts are recorded in [prompts.md](prompts.md).
The Library sort label was corrected to Recently added to match the example order.
The Activity journal icons were corrected to sage; pink remains reserved for unread updates.

The implementation in `src/Winnow.App/Views/Fullscreen/` also covers search and filtering,
text entry, list and metadata tools, and controller input for file selection and embedded
browser windows. The screen inventory and native-input limitations are in the visual spec.

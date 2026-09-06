## Notes

Here is where notes/observations will be recorded with the intent that they be addressed down the line with Claude

### Features
- Option to hide/remove games from Winnow
- Option to enable/disable explicit 18+ content
- Option to manually find and assign metadata through IGDB
- Loading indicator when metadata is being fetched
- Gamepad compatible full-screen mode (with a clock, controller battery aware if possible)
- Open folder button to custom theme location
- Add to list from details view
- Open patch notes in a contained webview
- List view should still have icons on the left side for each game

### Bugs
- Drop the over-explanatory text blurbs throughout the interface. Explanations should be short, straightforward, and unambiguous. No more than a few words.
- Transparency block has wayyyyy too much information. Users dngaf about this stuff. It's great for our design docs but don't put it in the UI
- Naming conventions need some work
  - Patched Since > Patched
  - Bounced Off > Bounced ? Not sure on this one, it reads like you quit it but it's currently capturing games in active play
  - Stores > Platforms
- The flip side of the card and details should have some sort of alternate art dim underneath the information
- Option to add games manually for things outside the supported platforms
- Density slider is counterintuitive, higher "density" implies more cards but it really makes the card size larger
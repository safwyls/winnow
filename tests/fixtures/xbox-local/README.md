# Xbox local package fixtures

Captured on 2026-09-17. These files contain public package/product identities, not
user identities, credentials, ownership or entitlement records.

- `solitaire-appx.xml` and `solitaire-xboxservices.json` were copied from the current-user
  Microsoft Store registration of Microsoft Solitaire Collection 4.26.7290.0 to a private
  temporary directory before inspection. They preserve the original XML namespaces,
  application/executable fields, localized resource references and numeric Xbox title ID.
  Neither file contains an account identifier; no launcher file was modified.
- `achievements-microsoftgame.config` is a captured Microsoft GDK sample configuration from
  [Xbox-GDK-Samples](https://github.com/microsoft/Xbox-GDK-Samples/blob/main/Samples/Live/Achievements/MicrosoftGameConfig.mgc).
  It is a development sample, not a capture from an installed retail GDK game. The tests supply
  a minimal companion Appx manifest using that same package identity. Its eight-character
  `TitleId` is hexadecimal, unlike the numeric `TitleId` in `xboxservices.config`.

Tests mutate these shapes to exercise traversal, duplicate IDs, DTDs, excessive XML depth,
development-only executables, DLC, multiple applications and partial scans. Those mutations
are adversarial test data, not observed launcher output. Retail GDK activation and protected
WindowsApps process tracking still require validation with an installed game.

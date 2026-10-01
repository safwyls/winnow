# Original theme token fixture

This test-only exporter runs the original C# theme implementation. It has no project reference
to the Avalonia app and does not start an application or backend. Normal Electron tests read
the generated JSON and require neither .NET nor Avalonia.

Before exporting, verify that `src/Winnow.App/Themes` matches the frozen migration source
`cf45d9f1127243a987d3cf6e664a32fc767ecb67` and use a matching built `Winnow.dll` with its
dependencies beside it. From the repository root:

```powershell
git diff cf45d9f1127243a987d3cf6e664a32fc767ecb67 -- src/Winnow.App/Themes
dotnet run --project src/Winnow.Electron/scripts/theme-contracts/ThemeContracts.csproj --artifacts-path .tmp/theme-contract-export -- <matching-output>/Winnow.dll .tmp/theme-tokens.json
```

The matrix follows `ThemeJsonTests.Every_token_matches_at_every_slider_position`: every
bundled theme, both layouts, both wall-translucency settings, and 0–100% in five-point steps.
The exporter verifies that JSON round-tripping preserves the dictionary size and every color
before writing the original values. Color strings use CSS `#RRGGBBAA` byte order; the complete
theme JSON is included for the independent Electron parser. This preserves all original token
names, including tokens that require an explicit mapping to browser CSS properties.

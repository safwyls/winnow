/** Match the bundled compositions' actual consumers; keep the shared contract available to installed themes. */
export function appearanceCapabilities(compositionId: string) {
  const avalon = compositionId === 'avalon'
  const afterglow = compositionId === 'afterglow'
  const catalogue = compositionId === 'catalogue'
  const rift = compositionId === 'rift'
  const external = !avalon && !afterglow && !catalogue && !rift
  return {
    roleTypography: avalon,
    fitUltrawide: avalon,
    fontPreset: !avalon,
    spacing: afterglow || external,
    artworkShade: afterglow || external,
    artworkMaterials: rift || catalogue || external,
    navigation: afterglow || external,
    libraryCards: afterglow || catalogue || external,
    gameDetails: !avalon,
    discoverSections: afterglow || external,
  }
}

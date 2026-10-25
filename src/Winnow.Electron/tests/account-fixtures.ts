import type { AccountStats } from '../src/renderer/features/Accounts'

// Same captured statistics as AccountStatsSummaryTests.Currency_charts_switch_keep_focus_and_render_without_horizontal_overflow.
export function accountChartGroup(symbol: string, factor: number): AccountStats {
  return {
    source: 'steam',
    hasAnything: true,
    isSingleCurrency: true,
    currencySymbol: symbol,
    currencyGroups: [],
    currencies: [{ symbol, transactionCount: 90 }],
    transactionCount: 90,
    licenseCount: 0,
    knownAccountCount: 1,
    unknownAccountFactCount: 0,
    transactionsWithoutCurrency: 0,
    grossProductTransactionCount: 80,
    grossProductSpendCents: factor * 13000,
    refundedProductTransactionCount: 5,
    refundedProductSpendCents: factor * 1000,
    netProductTransactionCount: 75,
    netProductSpendCents: factor * 12000,
    purchases: { count: 60, cents: factor * 9000 },
    giftPurchases: { count: 10, cents: factor * 2000 },
    inGamePurchases: { count: 5, cents: factor * 1000 },
    bundlePurchases: { count: 15, cents: factor * 5000 },
    refundTransactions: { count: 0, cents: 0 },
    walletCreditPurchases: { count: 0, cents: 0 },
    walletCreditRedemptions: { count: 0, cents: 0 },
    discountedPurchases: { count: 0, cents: 0 },
    discountedPurchaseListCents: 0,
    undatedNetSpendCents: 0,
    undatedNetTransactionCount: 0,
    licenseAcquisitions: [],
    spendByYear: [
      [2020, 10, 1000],
      [2021, 15, 2000],
      [2022, 10, 1500],
      [2023, 20, 3500],
      [2024, 10, 2500],
      [2025, 10, 1500],
    ].map(([year, transactionCount, cents]) => ({ year, transactionCount, cents: cents * factor })),
  }
}
export function accountChartFixture(): AccountStats {
  return {
    ...accountChartGroup('$', 10),
    isSingleCurrency: false,
    currencySymbol: null,
    transactionCount: 181,
    transactionsWithoutCurrency: 1,
    grossProductTransactionCount: 160,
    refundedProductTransactionCount: 10,
    netProductTransactionCount: 150,
    bundlePurchases: { count: 30, cents: 0 },
    currencies: [
      { symbol: '$', transactionCount: 90 },
      { symbol: '€', transactionCount: 90 },
    ],
    currencyGroups: [accountChartGroup('$', 10), accountChartGroup('€', 2)],
    licenseCount: 230,
    licenseAcquisitions: [
      { kind: 'steam_store', count: 120 },
      { kind: 'retail', count: 70 },
      { kind: 'complimentary', count: 30 },
      { kind: 'gift', count: 10 },
    ],
  }
}

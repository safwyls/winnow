# Storefront response fixtures

`epic-namespace-moonlighter.json` is the anonymous response recorded on 2026-09-06 from
Epic's GraphQL `Catalog.catalogNs` lookup for namespace `bec822fb982843c3be794d440728336b`,
requesting product-home mappings. The static productmapping response omitted this namespace
while this lookup returned the active Moonlighter page. It carries no account data or secrets.

The HTTP tests use this canned response and never call the live service.

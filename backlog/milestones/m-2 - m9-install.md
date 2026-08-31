---
id: m-2
title: "M9-INSTALL"
---

## Description

Install and uninstall management. Winnow delegates installation to the owning store client (steam://install/, Galaxy, the Epic launcher) and reflects state changes back into the database. Winnow never reimplements download, patching, or CDN auth.

Exit criteria: install and uninstall commands for Steam and Epic games delegate to the store's own client; installed/uninstalled state reflects back into the library view.

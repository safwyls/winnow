# Epic sign-in origins — 2026-09-06

The live https://www.epicgames.com/id/login page displayed nine provider buttons:
PlayStation Network, Xbox network, Nintendo Account, Google, Steam, Disney,
Sign in with Apple, Facebook and LEGO Account. No previously listed provider
was absent. This is the English page observed from this machine; regional and
account-specific choices can differ.

The page's loaded public bundle supplied the authorization origins below:
https://static-assets-prod.unrealengine.com/account-portal/static/assets/index-uqW5OxEf.js

| Provider | Authorization origin |
|---|---|
| PlayStation | https://ca.account.sony.com |
| Xbox | https://login.live.com |
| Nintendo | https://accounts.nintendo.com |
| Google | https://accounts.google.com |
| Steam | https://steamcommunity.com |
| Disney | https://login.disney.com |
| Apple | https://appleid.apple.com |
| Facebook | https://www.facebook.com |
| LEGO | https://identity.lego.com |

Disney and LEGO were missing from Winnow's render-only allowlist and are added.
The bundle also names https://talon-website-prod.ecosec.on.epicgames.com as its
current captcha origin. The older captcha origin and Xbox/PlayStation continuation
origins remain for existing launcher flows; their continued use was not established
by this observation. No whole provider is flagged for removal.

No provider account was signed in, linked or consented during this check. The
in-app browser did not open a provider popup, so authorization destinations were
verified from the live page's public configuration rather than by completing
nine external sign-ins. Epic's own support pages independently describe
[Disney](https://www.epicgames.com/help/c-45487929/c-38854402/a20048119?lang=en-US)
and [LEGO](https://www.epicgames.com/help/c-45487929/c-38854402/a11428482?lang=en-US).

All added origins are navigable only: no bridge, page-message trust or body
harvesting is granted. Regression tests assert that distinction and reject
suffix-lookalike origins. Recheck the visible choices and public configuration
before a later release; the provider list is not a stable API contract.
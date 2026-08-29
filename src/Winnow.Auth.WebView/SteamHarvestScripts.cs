using System.Globalization;

namespace Winnow.Auth.WebView;

/// <summary>
/// The scripts the harvest session runs inside a Steam account page.
///
/// <para><b>None of these is injected.</b> The sign-in prompt uses
/// <c>AddScriptToExecuteOnDocumentCreated</c>, which runs in every document of
/// the session and therefore carries its own origin guard. Nothing here does:
/// every one of these runs through <c>ExecuteScriptAsync</c>, which reaches only
/// the top-level document, and only after the host has asked
/// <see cref="Winnow.Core.Auth.SteamAccountPagePolicy.AllowsHarvest"/> about that
/// document. The two-paths-only rule is enforced at the call site rather than
/// inside the script because there is no path by which one of these lands in a
/// page the host did not choose.</para>
///
/// <para>Public so a test can read them. Every one is a total function: it
/// catches its own exceptions and answers with a shape the host can parse, so a
/// Steam redesign degrades the capture rather than throwing into the page.</para>
/// </summary>
public static class SteamHarvestScripts
{
    /// <summary>The global the captured document is parked in between chunks.</summary>
    private const string CaptureSlot = "__winnowSteamCapture";

    /// <summary>
    /// Whether the page is being rendered for a signed-in account.
    ///
    /// <para>Two positive marks and one negative one, and the negative one is
    /// what the answer actually rests on. Steam's header ids are markup that
    /// Valve may rename at any time, so treating their absence as "signed out"
    /// would turn a cosmetic redesign into a harvest that never captures
    /// anything. A password field is a far more durable signal, and its absence
    /// on a page whose URL is an account page is enough: the worst case is a
    /// useless document the parser rejects, rather than a session that hangs.</para>
    /// </summary>
    public const string SignedInProbe = """
        (function () {
            try {
                var mark = document.getElementById('account_pulldown')
                    || document.querySelector('a[href*="/logout"]')
                    || document.querySelector('#global_action_menu');
                var loginForm = document.querySelector('input[type="password"]');
                return { signedIn: !!mark || !loginForm, mark: !!mark, loginForm: !!loginForm };
            } catch (e) {
                return { signedIn: false, mark: false, loginForm: false };
            }
        })();
        """;

    /// <summary>
    /// The state of the purchase-history load-more control, and how much is
    /// currently rendered.
    ///
    /// <para>The control is found by id first and by its own label second.
    /// Neither selector is verified against a live page (no authenticated
    /// session existed when this was written), so the fallback is not
    /// belt-and-braces, it is the half that is likely to survive.</para>
    ///
    /// <para>Row counting prefers the transactions table and falls back to every
    /// row in the document. The number is only ever compared with itself, to
    /// answer "did that click do anything", so an over-count is harmless as long
    /// as it is consistent.</para>
    /// </summary>
    public const string LoadMoreProbe = """
        (function () {
            try {
                return { present: !!__winnowLoadMore(), rows: __winnowRows() };
            } catch (e) {
                return { present: false, rows: 0 };
            }
        })();
        """;

    /// <summary>Clicks the load-more control. Answers whether there was one to click.</summary>
    public const string ClickLoadMore = """
        (function () {
            try {
                var control = __winnowLoadMore();
                if (!control) { return false; }
                control.click();
                return true;
            } catch (e) {
                return false;
            }
        })();
        """;

    /// <summary>
    /// The helpers the probe and the click share, defined once per document.
    ///
    /// <para>Run before either of them. Kept separate rather than repeated inside
    /// each script so that "what counts as the load-more control" is one
    /// definition, the thing most likely to need changing once a real page has
    /// been seen.</para>
    /// </summary>
    public const string DefineHelpers = """
        (function () {
            if (window.__winnowLoadMore) { return true; }

            function visible(el) {
                if (!el) { return false; }
                if (el.disabled) { return false; }
                var style = window.getComputedStyle(el);
                if (style && (style.display === 'none' || style.visibility === 'hidden')) { return false; }
                return el.getClientRects().length > 0;
            }

            window.__winnowLoadMore = function () {
                var byId = document.getElementById('load_more_button');
                if (visible(byId)) { return byId; }

                var candidates = document.querySelectorAll(
                    '[id*="load_more"], [class*="load_more"], [class*="loadMore"], button, a');
                for (var i = 0; i < candidates.length; i++) {
                    var el = candidates[i];
                    var text = (el.textContent || '').trim();
                    if (text.length > 40) { continue; }
                    if (!/load\s*more/i.test(text)) { continue; }
                    if (visible(el)) { return el; }
                }
                return null;
            };

            window.__winnowRows = function () {
                // VERIFIED 2026-08-29 against a real signed-in purchase-history
                // page: the transactions table is table.wallet_history_table and
                // each transaction is a tr.wallet_table_row inside it. The id
                // this used to look for, 'store_transactions', is not an id on
                // this page at all — it is a fragment of the wallet-balance href
                // in the global header (/account/store_transactions/), which is
                // present on every store page and is not a table. getElementById
                // therefore always returned null and the count silently fell
                // through to every tr in the document.
                var rows = document.querySelectorAll(
                    'table.wallet_history_table tbody tr.wallet_table_row');
                if (rows.length > 0) { return rows.length; }

                var table = document.querySelector('table.wallet_history_table');
                return table ? table.querySelectorAll('tr').length
                             : document.querySelectorAll('tr').length;
            };

            // VERIFIED 2026-08-29: the LICENCES page has no load-more control.
            // It paginates, with a.license_paginator_next carrying a
            // continuationToken and an offset, and a
            // ".license_paginator_ctn span" reading "Showing licenses 1-100 of
            // 979". A capture that only reads the first document sees 100 rows
            // of however many the account has.
            window.__winnowLicensesNext = function () {
                var link = document.querySelector('a.license_paginator_next');
                return (link && visible(link)) ? link.href : null;
            };

            window.__winnowLicensesTotal = function () {
                var spans = document.querySelectorAll('.license_paginator_ctn span');
                for (var i = 0; i < spans.length; i++) {
                    var m = /([\d,]+)\s*-\s*([\d,]+)\s+of\s+([\d,]+)/.exec(spans[i].textContent || '');
                    if (m) {
                        return {
                            from: parseInt(m[1].replace(/,/g, ''), 10),
                            to: parseInt(m[2].replace(/,/g, ''), 10),
                            total: parseInt(m[3].replace(/,/g, ''), 10)
                        };
                    }
                }
                return null;
            };

            window.__winnowLicenceRows = function () {
                return document.querySelectorAll(
                    'table.account_table tr td.license_date_col').length;
            };

            return true;
        })();
        """;

    /// <summary>
    /// The licences page's pagination state: the next page's URL, the
    /// "showing X-Y of Z" counts, and how many rows this document holds.
    /// </summary>
    public const string LicensesPaginatorProbe = """
        (function () {
            try {
                return {
                    nextUrl: __winnowLicensesNext(),
                    counts: __winnowLicensesTotal(),
                    rows: __winnowLicenceRows()
                };
            } catch (e) {
                return { nextUrl: null, counts: null, rows: 0 };
            }
        })();
        """;

    /// <summary>
    /// The licences walk: fetches the next paginator page and merges its rows
    /// into the live document.
    ///
    /// <para><b>Why the page is assembled here rather than captured page by
    /// page.</b> <see cref="Winnow.Core.Ingest.SteamAccountPages"/> holds one
    /// document per page kind, and it holds it identically for the embedded
    /// session and for files the user saved by hand. Collecting ten documents
    /// would have made the harvested shape different from the saved-file shape
    /// and pushed a multi-document concept into the parser and the file loader
    /// for the benefit of one of the two routes. Splicing the rows in before the
    /// capture keeps one document, which is what a user who saved the page would
    /// have produced had Steam shown them all at once.</para>
    ///
    /// <para><b>The paginator is replaced, not merely followed.</b> The parser
    /// decides whether a document is a partial view from
    /// <c>a.license_paginator_next</c> and the "Showing X-Y of Z" span. Leaving
    /// the first page's paginator in place would leave every complete walk
    /// looking truncated, so the fetched page's paginator replaces it and the
    /// last one merged is the one the parser reads.</para>
    ///
    /// <para>The fetch is same-origin with credentials, which is the licences
    /// page asking Steam for its own next page. Its outcome is parked in a global
    /// rather than returned, because a script that returns a promise crosses
    /// WebView2's IPC as an empty object; the host polls
    /// <see cref="LicensesWalkState"/> instead.</para>
    /// </summary>
    public const string LicensesWalkHelpers = """
        (function () {
            if (window.__winnowLicensesWalk) { return true; }

            window.__winnowLicensesWalk = { pending: false, ok: false, added: 0, error: null };

            function table(doc) {
                var tables = doc.querySelectorAll('table.account_table');
                for (var i = 0; i < tables.length; i++) {
                    if (tables[i].querySelector('th.license_date_col')) { return tables[i]; }
                }
                return null;
            }

            function merge(fetched) {
                var from = table(fetched);
                var into = table(document);
                if (!from || !into) { return -1; }

                var sink = (into.tBodies && into.tBodies.length)
                    ? into.tBodies[into.tBodies.length - 1]
                    : into;

                var rows = from.querySelectorAll('tr');
                var added = 0;
                for (var i = 0; i < rows.length; i++) {
                    // The header row is how the parser recognises the table. One
                    // copy of it, from the first page, is what it should find.
                    if (rows[i].querySelector('th')) { continue; }
                    sink.appendChild(document.importNode(rows[i], true));
                    added++;
                }

                var fresh = fetched.querySelectorAll('.license_paginator_ctn');
                var stale = document.querySelectorAll('.license_paginator_ctn');
                for (var j = 0; j < stale.length; j++) {
                    if (j < fresh.length) {
                        stale[j].parentNode.replaceChild(
                            document.importNode(fresh[j], true), stale[j]);
                    } else {
                        stale[j].parentNode.removeChild(stale[j]);
                    }
                }

                return added;
            }

            window.__winnowLicensesFetchNext = function () {
                var state = window.__winnowLicensesWalk;
                if (state.pending) { return true; }

                var next = window.__winnowLicensesNext ? window.__winnowLicensesNext() : null;
                if (!next) { return false; }

                state.pending = true;
                state.ok = false;
                state.added = 0;
                state.error = null;

                try {
                    fetch(next, { credentials: 'include', cache: 'no-store' })
                        .then(function (r) { return r.text(); })
                        .then(function (text) {
                            var added = merge(new DOMParser().parseFromString(text, 'text/html'));
                            if (added < 0) {
                                state.error = 'no licences table in the fetched page';
                            } else {
                                state.added = added;
                                state.ok = true;
                            }
                            state.pending = false;
                        })
                        .catch(function () {
                            state.error = 'the next page could not be fetched';
                            state.pending = false;
                        });
                } catch (e) {
                    state.error = 'the next page could not be requested';
                    state.pending = false;
                }

                return true;
            };

            return true;
        })();
        """;

    /// <summary>Starts a fetch of the next licences page. Answers whether there was one to fetch.</summary>
    public const string FetchNextLicensesPage = """
        (function () {
            try {
                return !!__winnowLicensesFetchNext();
            } catch (e) {
                return false;
            }
        })();
        """;

    /// <summary>Where the in-flight licences fetch has got to.</summary>
    public const string LicensesWalkState = """
        (function () {
            try {
                var s = window.__winnowLicensesWalk;
                if (!s) { return { pending: false, ok: false, added: 0, error: 'not started' }; }
                return { pending: !!s.pending, ok: !!s.ok, added: s.added | 0, error: s.error || null };
            } catch (e) {
                return { pending: false, ok: false, added: 0, error: 'unavailable' };
            }
        })();
        """;

    /// <summary>
    /// Takes the document, parks it in a global and answers its length in
    /// characters.
    ///
    /// <para>Parked rather than returned because a script result crosses
    /// WebView2's IPC as one JSON string, and an account with a decade of
    /// purchases produces a document large enough to make that a gamble.
    /// <see cref="Chunk"/> then reads it back in pieces of a size that is not
    /// one.</para>
    /// </summary>
    public static string BeginCapture => $$"""
        (function () {
            try {
                window.{{CaptureSlot}} = document.documentElement
                    ? document.documentElement.outerHTML
                    : '';
                return window.{{CaptureSlot}}.length;
            } catch (e) {
                window.{{CaptureSlot}} = '';
                return -1;
            }
        })();
        """;

    /// <summary>One slice of the parked document.</summary>
    /// <param name="offset">Character offset to read from.</param>
    /// <param name="length">How many characters to read.</param>
    public static string Chunk(int offset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        var start = offset.ToString(CultureInfo.InvariantCulture);
        var end = ((long)offset + length).ToString(CultureInfo.InvariantCulture);

        return $$"""
            (function () {
                try {
                    var held = window.{{CaptureSlot}};
                    return held ? held.substring({{start}}, {{end}}) : '';
                } catch (e) {
                    return '';
                }
            })();
            """;
    }

    /// <summary>
    /// Drops the parked document.
    ///
    /// <para>The page is about to be navigated away from or destroyed with the
    /// session, so this is not what keeps the capture out of memory. It is what
    /// keeps a copy of the user's purchase history out of the <em>page's</em>
    /// memory, where the page's own script could reach it.</para>
    /// </summary>
    public static string EndCapture => $"window.{CaptureSlot} = null;";
}

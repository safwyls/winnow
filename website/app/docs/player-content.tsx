import { sitePath } from '@/lib/site-path';
import type { DocArticle } from './content-types';

export const setupArticle: DocArticle = {
  slug: 'setup',
  title: 'Set up your library',
  description: 'Install Winnow, connect your launchers, and find your first game to return to.',
  audience: 'Players',
  sections: [
    { id: 'install', title: '1. Install Winnow', content: <>
      <p>Winnow keeps your library on your device. You do not need a Winnow account, a subscription, or an API key to begin.</p>
      <ol>
        <li>Open <a href="https://github.com/safwyls/winnow/releases">GitHub Releases</a> and choose a published release for your platform.</li>
        <li>On Windows x64, run the <code>-setup.exe</code> installer. For a portable copy, extract the entire <code>win-x64.zip</code> archive and run <code>Winnow.exe</code>. Both include the .NET runtime.</li>
        <li>On Ubuntu 24.04 x64, install the <code>.deb</code> with the command below, then open Winnow from the application menu. The Linux portable archive is an alternative; see the <a href="https://github.com/safwyls/winnow/blob/main/docs/releases.md#platform-limits">native dependency list</a>.</li>
      </ol>
      <pre><code>{'sudo apt install ./Winnow-<version>-linux-x64.deb'}</code></pre>
      <p>Windows packages are currently unsigned. Windows browser sign-in requires Microsoft’s Evergreen WebView2 Runtime. Linux currently has more limited Epic/GOG discovery and no embedded sign-in or persistent secret storage; start with local Steam discovery there.</p>
    </> },
    { id: 'first-run', title: '2. Walk through first-run setup', content: <>
      <p>Open your launchers and let their libraries finish updating before starting Winnow. Winnow reads their local files without modifying them. The window opens after the local scan; descriptions, artwork, and update information arrive in the background.</p>
      <ol>
        <li>The setup wizard walks through IGDB, Steam, Epic, GOG, theme, application preferences, and library preferences.</li>
        <li>Use <strong>Continue</strong> to advance or <strong>Back</strong> to revisit a step. Optional connections can wait: choose <strong>Skip this step</strong>, or <strong>Skip setup</strong> to go straight to your library.</li>
        <li>Preferences save as you change them. Credential forms require their explicit <strong>Save</strong> action; skipping a step discards unsaved secret fields.</li>
        <li>Allow the first background pass time to finish. A large library can take a minute or two to begin filling out.</li>
      </ol>
      <p>If you close the app during setup, it resumes the unfinished step. To revisit it later, use <strong>Settings → Application → Run setup again</strong> on either desktop or fullscreen.</p>
    </> },
    { id: 'steam', title: '3. Connect Steam', content: <>
      <p>Local Steam files provide installed games and locally recorded playtime and last-played information. An optional connection adds owned games that have never been installed on this PC.</p>
      <ol>
        <li>Open <strong>Settings → Platforms → Steam</strong> and check the local-file and connection status.</li>
        <li>Choose one connection method: <strong>Sign in to Steam</strong> opens Steam’s own sign-in flow; <strong>Steam Web API key</strong> lets you save a key instead. Read the explanation beside each method before choosing.</li>
        <li>For browser sign-in, review the consent screen and decide whether to allow purchase-history capture. Complete Steam’s authentication in the window that opens.</li>
        <li>For an API key, choose <strong>Get a key</strong> if needed, enter your key in the Steam configuration, and choose <strong>Save</strong>. This method cannot read purchase history; the account filter becomes available after an import confirms your account.</li>
        <li>Return to Platforms and confirm the connection status. Give background synchronization time to add the rest of your collection.</li>
      </ol>
      <p>You only need one method. If both are configured, scheduled updates use the API key. A sign-in session may need renewing; use <strong>Sign in again</strong> when its status asks you to. Purchase-history tools are in the Steam settings entry.</p>
    </> },
    { id: 'epic-gog', title: '4. Add Epic and GOG', content: <>
      <h3>Epic Games</h3>
      <ol>
        <li>On Windows, launch the Epic Games Launcher and let it update its local library cache.</li>
        <li>Open <strong>Settings → Platforms → Epic</strong>. Local discovery reads cached owned titles and installation state.</li>
        <li>For account inventory and acquisition dates, choose <strong>Sign in to Epic</strong> and complete Epic’s sign-in window. Check the status on returning to Winnow.</li>
      </ol>
      <h3>GOG</h3>
      <ol>
        <li>Let GOG Galaxy synchronize on Windows before Winnow scans.</li>
        <li>Open <strong>Settings → Platforms → GOG</strong> to inspect discovery status. Winnow reads Galaxy ownership, available play facts, and installation state, with a registry fallback for installations.</li>
      </ol>
      <p>There is no GOG sign-in connection in Winnow. These integrations depend on what the launchers have recorded locally; connecting another store does not fill gaps in GOG’s data.</p>
    </> },
    { id: 'first-game', title: '5. Find something to play', content: <>
      <ol>
        <li>Open <strong>Feed</strong> on desktop or <strong>For you</strong> in fullscreen. Read the reason shown with a recommendation to see why it appeared.</li>
        <li>On desktop, hover a cover to preview its details. Click the artwork to open the game’s details. Feed actions let you add it to a list, postpone it with <strong>Not now</strong>, or mark it <strong>Not interested</strong>.</li>
        <li>Choose <strong>Play</strong> for an installed game. Keep Winnow running to let session tracking observe your play.</li>
        <li>Use the full library when you already know what you want. Search, filter, and choose a <a href={sitePath('/docs/configuration/#library-sort')}>default sort</a> that suits your collection.</li>
      </ol>
      <p>The <strong>Patched</strong> group builds gradually: the update poller spreads a sweep across seven days. An empty group on the first day does not mean setup failed.</p>
    </> },
    { id: 'setup-troubleshooting', title: 'If something is missing', content: <>
      <dl>
        <dt>Only installed Steam games appear</dt><dd>Check Steam’s connection status. Local files cannot supply every untouched game in an account; connect a Web API key or sign in.</dd>
        <dt>The browser sign-in window will not open</dt><dd>On Windows, check that the Evergreen WebView2 Runtime is installed. Steam’s API-key method does not require that browser. Embedded sign-in is not available on Linux.</dd>
        <dt>A game has no cover or summary yet</dt><dd>Let enrichment finish and check your connection. IGDB and additional artwork providers are optional; follow the <a href={sitePath('/docs/configuration/#metadata')}>metadata setup</a> to add more sources.</dd>
        <dt>A game is present but not visible</dt><dd>Clear browsing filters and inspect Library settings for hidden games, explicit-content settings, and the content age limit. Non-game entries and grouped expansions can also change what appears.</dd>
      </dl>
      <p>For a reproducible failure, note the version and source commit in <strong>Settings → Application → About Winnow</strong>. Include the platform, installation method, and exact steps in a <a href="https://github.com/safwyls/winnow/issues">GitHub issue</a>. Logs are in the data directory’s <code>logs</code> folder; review anything you attach.</p>
    </> },
  ],
};

export const configurationArticle: DocArticle = {
  slug: 'configuration',
  title: 'Make Winnow yours',
  description: 'Tune artwork, sorting, fullscreen controls, updates, and local data storage.',
  audience: 'Players',
  sections: [
    { id: 'settings', title: 'Find the right settings', content: <>
      <p>On desktop, open the settings cog at the bottom of the navigation rail. In fullscreen, switch to <strong>Settings</strong> from the main navigation. Both surfaces share account connections, library preferences, plugin settings, theme, cover-art mode, and cover dimming.</p>
      <p>Browsing positions and filters are separate. Fullscreen also has its own text size, interface scale, screen margins, and motion preferences. Changing the TV layout does not resize the desktop interface.</p>
    </> },
    { id: 'appearance', title: 'Choose a theme', content: <>
      <ol>
        <li>Open <strong>Settings → Appearance</strong>. On desktop, select a theme from the previews. In fullscreen, open <strong>Theme</strong> and choose a name.</li>
        <li>The new palette applies immediately and is shared between desktop and fullscreen. Return to your library to see it alongside your artwork.</li>
        <li>In fullscreen Appearance, use <strong>Dim dormant covers</strong> to choose whether games you have not played recently look quieter. This preference also applies to desktop.</li>
      </ol>
      <p>Cover dimming is a visual cue, not a filter: it does not hide games or change their play history. To change which games appear, use the library controls below.</p>
    </> },
    { id: 'metadata', title: 'Add IGDB metadata and artwork', content: <>
      <p>Winnow works without IGDB credentials. Adding them provides another source of game details and artwork alongside the built-in Steam sources.</p>
      <ol>
        <li>Open <strong>Settings → Metadata &amp; artwork → IGDB metadata</strong>.</li>
        <li>Choose <strong>Get IGDB credentials</strong> to open the Twitch developer console. Follow the linked IGDB registration instructions, register a Confidential application, and generate a client secret.</li>
        <li>Enter the <strong>Client ID</strong> and <strong>Client secret</strong>, then choose <strong>Save credentials</strong>.</li>
        <li>The secret field clears after saving. Winnow queues metadata enrichment behind any active startup sync or credential refresh; watch the title-bar progress.</li>
      </ol>
      <p>Saving stores the pair; it does not validate it with Twitch. If details do not arrive, recheck the credentials. <strong>Remove saved credentials</strong> removes Winnow’s saved pair, but environment or local configuration values remain available as a fallback. Persistent secret storage currently requires Windows.</p>
    </> },
    { id: 'artwork', title: 'Choose artwork sources and cover fit', content: <>
      <ol>
        <li>For SteamGridDB backgrounds, open <strong>Settings → Plugins → SteamGridDB</strong> and save your own API key. This provider ships enabled; it matches games with known Steam app IDs, including linked copies from other stores.</li>
        <li>Open <strong>Settings → Metadata &amp; artwork</strong> and reorder the available backdrop sources. The order applies immediately to desktop and fullscreen. A background you explicitly selected for a game takes priority.</li>
        <li>On desktop, open <strong>Display → Cover art</strong> in the library toolbar. In fullscreen, use <strong>Settings → Appearance → Cover art</strong>.</li>
        <li>Choose <strong>Fit</strong> to show the entire image, including padding when its proportions differ from the card, or <strong>Fill</strong> to crop the image to the card.</li>
      </ol>
      <p>Cover fit is shared across both interfaces and defaults to Fit. It changes presentation, not the underlying downloaded artwork. Backdrop-source order does not change which provider supplies metadata fields.</p>
    </> },
    { id: 'library-sort', title: 'Set your default library order', content: <>
      <ol>
        <li>Open <strong>Settings → Library → Default library sort</strong> on either surface.</li>
        <li>Choose <strong>Dormant longest</strong>, <strong>Recently played</strong>, <strong>Playtime high→low</strong>, <strong>Playtime low→high</strong>, <strong>Name A–Z</strong>, or <strong>Name Z–A</strong>.</li>
        <li>The preference saves immediately and is used when Winnow starts. Use browsing sort controls whenever you want a different order for the current view.</li>
      </ol>
      <p>For a familiar alphabetical collection, try Name A–Z. For a quick return to a current game, try Recently played.</p>
    </> },
    { id: 'library-visibility', title: 'Decide what your library shows', content: <>
      <p><strong>Settings → Library</strong> contains hidden-game management and hand-added games. On fullscreen, open <strong>Library tools</strong> for those management screens. Choose <strong>Unhide</strong> to bring a hidden entry back.</p>
      <p>For a game outside a supported launcher, use <strong>Add from a file</strong> or the hand-added game form. Review the title and optional metadata before saving. An executable path lets Winnow time its sessions; optional Steam or IGDB IDs help find artwork.</p>
      <p>Desktop’s <strong>Display</strong> menu and fullscreen’s Library settings also expose non-game visibility, expansion grouping, the content age limit, and the post-play journal prompt. The explicit-content setting filters adults-only sexual content; it is separate from an age rating for violence. Games without rating data remain visible.</p>
    </> },
    { id: 'fullscreen', title: 'Configure fullscreen for your display', content: <>
      <ol>
        <li>Press <kbd>F11</kbd>, choose the desktop Fullscreen icon, or press the controller’s Menu/Start button to enter fullscreen.</li>
        <li>Open <strong>Settings → Appearance</strong>. Set <strong>Interface scale</strong> to size covers, controls, and text together. Adjust <strong>Text size</strong> separately for readability from your seat.</li>
        <li>Use <strong>Screen margins</strong> if a TV cuts off content. Enable <strong>Fit ultrawide displays</strong> to use the screen’s full width. Choose <strong>Reduce motion</strong> if you prefer minimal animation.</li>
        <li>To launch here next time, enable <strong>Settings → Application → Start in fullscreen</strong>. Windows sign-in and explicit background launches still start quietly in the notification area.</li>
      </ol>
      <table><thead><tr><th>Control</th><th>Action</th></tr></thead><tbody>
        <tr><td>D-pad / left stick</td><td>Move between games and controls</td></tr>
        <tr><td>A / B</td><td>Select / go back</td></tr>
        <tr><td>LB / RB</td><td>Switch main screens</td></tr>
        <tr><td>LT / RT</td><td>Switch local sections or shelves</td></tr>
        <tr><td>X</td><td>Play the selected installed game</td></tr>
        <tr><td>Y</td><td>Use the action described in the footer</td></tr>
        <tr><td>View / Back</td><td>Search while browsing</td></tr>
        <tr><td>Menu / Start</td><td>Open the quick menu</td></tr>
        <tr><td>Right stick up / down</td><td>Scroll long content</td></tr>
      </tbody></table>
      <p>The footer reflects the current screen. For example, while the on-screen keyboard is open, X backspaces and RT presses Enter. Use F11 or <strong>Exit fullscreen</strong> in the quick menu to return to the desktop window.</p>
    </> },
    { id: 'updates', title: 'Keep Winnow up to date', content: <>
      <p>Open <strong>Settings → Application</strong> and find the update controls. Automatic checks and downloads are enabled by default; <strong>Include beta releases</strong> is off. Use <strong>Check for updates</strong> for a manual check.</p>
      <p>Supported Windows installations and portable Windows/Ubuntu copies offer <strong>Update and restart</strong>. Downloading alone does not close Winnow. Debian packages use the package manager; other unsupported portable locations offer a release link for manual replacement. Development and CI builds do not offer release upgrades.</p>
      <p>If an upgrade is interrupted, keep the data directory and update workspace. Install the same or a newer release, or follow the <a href="https://github.com/safwyls/winnow/blob/main/docs/releases.md#portable-replacement-and-recovery">recovery instructions</a>. Do not open an older binary against a database a newer version may have migrated.</p>
    </> },
    { id: 'data', title: 'Back up or isolate your library', content: <>
      <p>On Windows, the default data folder is <code>{'%LOCALAPPDATA%\\Winnow'}</code>. It contains <code>winnow.db</code>, artwork caches, themes, plugins, and logs. A portable application still uses the normal user data directory unless you explicitly choose another.</p>
      <ol>
        <li>Exit Winnow completely before making a manual backup, including any notification-area instance.</li>
        <li>Copy the entire data directory to your backup location. Keep any database sidecar files with the database.</li>
        <li>To test a copy without changing your usual library, start Winnow with an explicit data directory:</li>
      </ol>
      <pre><code>{'Winnow.exe --data-dir "C:\\Winnow-test-library"'}</code></pre>
      <p>From a source checkout:</p>
      <pre><code>{'dotnet run --project src/Winnow.App -- --data-dir "C:\\Winnow-test-library"'}</code></pre>
      <p>An empty directory creates an independent library; copy your backup there first if you want to inspect existing data. The argument redirects database files, caches, themes, plugins, and the browser profile. An unusable path stops startup rather than silently opening your usual library.</p>
      <p>Stored secrets use Windows current-user encryption. A copied database is not a portable credential export; reconnect services if the destination cannot decrypt them. Backups can contain library history and credentials, so keep them private.</p>
    </> },
  ],
};

export const pluginsArticle: DocArticle = {
  slug: 'plugins',
  title: 'Install and manage plugins',
  description: 'Add library sources, artwork, metadata, and recommendation shelves from trusted providers.',
  audience: 'Players',
  sections: [
    { id: 'what-plugins-do', title: 'What a plugin can add', content: <>
      <p>Provider plugins can import libraries, supply metadata and artwork, or add recommendation shelves. Their results use Winnow’s existing library cards, details, and galleries on desktop and fullscreen. SteamGridDB is the bundled artwork plugin.</p>
      <p>Plugins execute inside Winnow with its access to your device. Enable packages only from authors you trust. This SDK does not provide a security sandbox, a marketplace, automatic plugin updates, or custom replacement screens.</p>
    </> },
    { id: 'installation', title: 'Install a provider step by step', content: <>
      <ol>
        <li>Download the plugin package from its author. Check that it supports Winnow’s current plugin API, version 1.</li>
        <li>Open <strong>Settings → Plugins → Open plugins folder</strong>. This opens <code>plugins</code> inside the selected data directory.</li>
        <li>Place the plugin ZIP in that folder, then restart Winnow. Alternatively, place an unpacked package in its own subdirectory. The package needs its manifest, entry DLL, and dependencies together.</li>
        <li>Return to Plugins settings. New third-party providers start disabled; inspect the entry, enable it, and restart again to activate it.</li>
        <li>Confirm the plugin’s name and version appear in the loaded-plugins summary. Enter its settings and credentials, then choose <strong>Save</strong>. For an active provider, saving queues a background refresh.</li>
      </ol>
      <p>Enabling or disabling takes effect after restart. The loaded-plugins summary describes this session, so it will not change just because you toggled a pending preference. Successful ZIP imports move the original archive into <code>plugins/.archives</code>.</p>
    </> },
    { id: 'steamgriddb', title: 'Example: configure SteamGridDB', content: <>
      <ol>
        <li>Open <strong>Settings → Plugins → SteamGridDB</strong>. This plugin already ships enabled, so no download is needed.</li>
        <li>Obtain your own key from <a href="https://www.steamgriddb.com/profile/preferences/api">SteamGridDB API preferences</a>, paste it into the plugin’s API-key field, and save.</li>
        <li>Wait for background artwork enrichment, or choose <strong>Refresh</strong> to queue another pass.</li>
        <li>Open <strong>Settings → Metadata &amp; artwork</strong> to choose where SteamGridDB sits in your backdrop-source order.</li>
      </ol>
      <p>SteamGridDB matches known Steam app IDs. A game without that link continues to use other artwork sources. Cached artwork remains available offline. Your explicitly selected per-game background still wins over the automatic source order.</p>
    </> },
    { id: 'update-remove', title: 'Update, disable, or remove a plugin', content: <>
      <dl>
        <dt>Disable</dt><dd>Turn off the provider in Plugins settings and restart. Its previously imported library facts and cached metadata remain.</dd>
        <dt>Update</dt><dd>Close Winnow, back up the existing package if you want to keep it, then replace its package files. Alternatively, remove the old plugin directory before placing a replacement ZIP in the plugins folder. Restart and check the loaded version.</dd>
        <dt>Uninstall</dt><dd>Close Winnow and remove the user plugin’s directory. Imported facts and cached metadata remain; the provider stops running.</dd>
      </dl>
      <p>ZIP imports do not overwrite existing packages or replace bundled plugins. Refresh updates provider data; it does not reload a changed DLL.</p>
    </> },
    { id: 'troubleshooting', title: 'Resolve installation and refresh problems', content: <>
      <dl>
        <dt>The package does not appear</dt><dd>Check the selected data directory and restart. A ZIP must contain <code>plugin.json</code> and the entry DLL at its root or inside one enclosing folder. An unpacked package belongs in its own directory.</dd>
        <dt>The ZIP remains in the plugins folder</dt><dd>Inspect the diagnostic in Plugins settings. Invalid manifests, incompatible APIs, duplicate IDs, unsafe archive paths, conflicting files, and oversized packages are rejected. Ask the author for a corrected package rather than editing arbitrary manifest fields.</dd>
        <dt>Enabled, but not loaded</dt><dd>Restart to apply the change, then read the current-session summary and diagnostics. A provider can also be disabled for the session after a timeout.</dd>
        <dt>Settings saved, but no new data</dt><dd>Confirm the provider is enabled and loaded. Check required credentials, allow startup synchronization to finish, then choose Refresh. A saved credential is not a guarantee that the external service accepts it.</dd>
        <dt>A secret will not save</dt><dd>Persistent secret storage is currently supported on Windows. Winnow refuses writes when it cannot protect a secret. On other hosts, an author may document environment-based configuration; see the SDK’s host-services reference.</dd>
      </dl>
      <p>When reporting a problem, include the Winnow version, plugin name/version, and diagnostic shown in settings. Do not include API keys or sign-in tokens. Authors can start with the <a href={sitePath('/docs/plugin-sdk/')}>plugin SDK guide</a>.</p>
    </> },
  ],
};

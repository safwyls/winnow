using System.Runtime.InteropServices;
using System.Text;
using Winnow.App.ViewModels;
using Winnow.Core.Reading;
using Winnow.Core.Repositories;

namespace Winnow.App.Services;

public enum LinkDestination { InApp, Browser, StoreClient }

public interface IStoreClientAvailability
{
    bool IsAvailable(string scheme);
}

/// <summary>Checks a registered Windows protocol handler and its executable without launching it.</summary>
public sealed class StoreClientAvailability : IStoreClientAvailability
{
    private const uint ProtocolAssociation = 0x1000;
    private const uint ExecutableAssociation = 2;
    public bool IsAvailable(string scheme)
    {
        if (!OperatingSystem.IsWindows() || scheme is not (GameLink.SteamScheme or GameLink.EpicScheme or GameLink.GogScheme)) return false;
        try
        {
            var executable = new StringBuilder(2048);
            uint length = (uint)executable.Capacity;
            return AssocQueryString(ProtocolAssociation, ExecutableAssociation, scheme, null, executable, ref length) == 0
                && File.Exists(executable.ToString());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or UnauthorizedAccessException) { return false; }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int AssocQueryString(uint flags, uint value, string association, string? extra,
        StringBuilder output, ref uint length);
}

public sealed record LinkOpenResult(bool Opened, string? Message = null);

public interface IGameLinkRouter
{
    Task<LinkOpenResult> OpenAsync(GameLink link, string title);
}

/// <summary>Chooses among supported reading routes; native game/installation actions keep their original target.</summary>
public sealed class GameLinkRouter(IUriDispatcher dispatcher, ISettingsRepository settings,
    IStoreClientAvailability clients, IPatchNotesReader? reader = null, PluginGameActionService? pluginActions = null) : IGameLinkRouter
{
    public const string SettingKey = "application.link_destination";
    public static LinkDestination Parse(string? value) => value switch
    {
        "browser" => LinkDestination.Browser,
        "store" => LinkDestination.StoreClient,
        _ => LinkDestination.InApp,
    };
    public static string Serialize(LinkDestination value) => value switch
    {
        LinkDestination.Browser => "browser",
        LinkDestination.StoreClient => "store",
        _ => "in-app",
    };

    public async Task<LinkOpenResult> OpenAsync(GameLink link, string title)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.PluginId is not null)
        {
            try
            {
                return link.Kind == GameLinkKind.Link && pluginActions is not null
                    && await pluginActions.ExecuteAsync(link.PluginOwnershipId, link)
                    ? new(true) : new(false, "This plugin action is unavailable.");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            { return new(false, "Could not open this link. Try again."); }
        }
        if (GameLink.Create(link.Label, link.Uri, kind: link.Kind) is null
            || !Uri.TryCreate(link.Uri, UriKind.Absolute, out var uri)) return new(false, "This link is unavailable.");
        try
        {
            if (link.IsLauncherProtocol)
            {
                // These targets explicitly name a client action, not an alternate reading route.
                return await Dispatch(uri);
            }

            var destination = Parse(await settings.GetAsync(SettingKey));
            string? fallback = null;
            if (destination == LinkDestination.InApp)
            {
                try
                {
                    if (reader is { IsAvailable: true }
                        && reader.Open(uri, title) == PatchNotesOutcome.Opened) return new(true);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
                fallback = "This page cannot open in Winnow. Opened in your browser.";
            }
            else if (destination == LinkDestination.StoreClient)
            {
                try
                {
                    if (SteamStoreTarget(uri) is { } clientUri && clients.IsAvailable(GameLink.SteamScheme)
                        && await dispatcher.OpenAsync(clientUri)) return new(true);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
                fallback = "This page cannot open in your store client. Opened in your browser.";
            }
            var result = await Dispatch(uri);
            return result.Opened ? result with { Message = fallback } : result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new(false, "Could not open this link. Try again.");
        }
    }

    private async Task<LinkOpenResult> Dispatch(Uri uri) => await dispatcher.OpenAsync(uri)
        ? new(true) : new(false, "Could not open this link. Try again.");

    internal static Uri? SteamStoreTarget(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "store.steampowered.com"
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length is 2 or 3 && parts[0] == "app" && GameLink.IsSteamAppId(parts[1])
            ? new Uri($"steam://store/{parts[1]}") : null;
    }
}

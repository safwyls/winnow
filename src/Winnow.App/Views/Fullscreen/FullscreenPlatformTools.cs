using Avalonia.Controls;
using Avalonia.Data;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenPlatformPage : FullscreenPage
{
    private readonly string _platform;
    private readonly TextBlock _notice = FullscreenUi.Text("", 28, "TextDim");
    private bool _disposed;
    public Task PendingRefresh { get; private set; } = Task.CompletedTask;
    public override string Title => _platform;

    public FullscreenPlatformPage(FullscreenContext context, string platform) : base(context)
    {
        _platform = platform;
        var stores = context.Shared.Stores;
        var body = FullscreenUi.Stack(FullscreenUi.Text(platform, 64));
        var controls = new List<Control[]>();
        void Bind(Control control, Avalonia.AvaloniaProperty property, string name)
            => control.Bind(property, new Binding(name) { Source = stores });
        void Text(string property, string? visible = null, string resource = "Text")
        {
            var text = FullscreenUi.Text("", 28, resource);
            Bind(text, TextBlock.TextProperty, property);
            if (visible is not null) Bind(text, IsVisibleProperty, visible);
            body.Children.Add(text);
        }
        Button Add(string text, Action action, string? visible = null)
        {
            var button = FullscreenUi.Button(text, action);
            if (visible is not null) Bind(button, IsVisibleProperty, visible);
            body.Children.Add(button); controls.Add([button]);
            return button;
        }
        void Label(Button button, string property)
        {
            Bind(button, ContentControl.ContentProperty, property);
            Bind(button, Avalonia.Automation.AutomationProperties.NameProperty, property);
        }
        async Task Run(Func<Task> action, string failure)
        {
            try { await action(); }
            catch (Exception) { if (!_disposed) _notice.Text = failure; }
            if (!_disposed && TopLevel.GetTopLevel(this) is not null) FocusInitial();
        }
        if (platform == "Steam")
        {
            Text(nameof(stores.SteamStatusLabel));
            Text(nameof(stores.SteamSessionHealthMessage), nameof(stores.SteamHasSession), "TextDim");
            Text(nameof(stores.SteamSignedInAccountText), nameof(stores.ShowSteamSignedInAccount), "TextDim");
            Text(nameof(stores.SteamConnectionMessage));
            Text(nameof(stores.SteamLocalMessage), resource: "TextDim");
            Add("Sign out of Steam", () => context.ShowActions(stores.SteamSignOutMessage,
                [new("Sign out", () => _ = Run(() => stores.SignOutOfSteamCommand.ExecuteAsync(null), "Couldn't sign out of Steam. Try again.")), new("Cancel", () => { })]), nameof(stores.SteamHasSession));
            var signIn = Add(stores.SteamSignInButtonText,
                () => context.Push(new FullscreenSteamConsentPage(context, () => { })), nameof(stores.ShowSteamSignInAction));
            Label(signIn, nameof(stores.SteamSignInButtonText));
            Bind(signIn, IsEnabledProperty, nameof(stores.SteamSignInAvailable));
            Text(nameof(stores.SteamSignInUnavailableMessage), nameof(stores.ShowSteamSignInUnavailable), "TextDim");
            Text(nameof(stores.SteamSignInBusyMessage), nameof(stores.ShowSteamSignInBusy), "TextDim");
            Text(nameof(stores.SteamSignInNoticeMessage), nameof(stores.ShowSteamSignInNotice), "TextDim");
            Text(nameof(stores.SteamSignInProblemMessage), nameof(stores.ShowSteamSignInProblem), "Amber");
            Add("Steam Web API key", () => context.Push(new FullscreenSteamApiKeyPage(context)));
            Add("Purchase history", () => context.Push(new FullscreenPurchaseHistoryPage(context)), nameof(stores.ShowPurchaseImport));
            Text(nameof(stores.AccountScopeMessage));
            Text(nameof(stores.AccountScopeCaveatMessage), resource: "TextDim");
            var scope = Add(stores.AccountScopeToggleLabel, () => _ = Run(async () =>
            {
                stores.ShowOwnAccountOnly = !stores.ShowOwnAccountOnly;
                await stores.PendingAccountScopeSave; await context.RefreshAsync();
            }, "Couldn't change account visibility. Try again."));
            Bind(scope, IsEnabledProperty, nameof(stores.CanChooseAccountScope));
            Bind(scope, Avalonia.Automation.AutomationProperties.ItemStatusProperty, nameof(stores.ShowOwnAccountOnly));
            Text(nameof(stores.AccountScopeBlockedMessage), nameof(stores.ShowAccountScopeBlocked), "TextDim");
        }
        else if (platform == "Epic")
        {
            Text(nameof(stores.EpicStatusLabel));
            Text(nameof(stores.EpicAccountLine), nameof(stores.ShowEpicAccountLine));
            Text(nameof(stores.EpicAnonymousMessage), nameof(stores.ShowEpicAnonymousLine));
            Text(nameof(stores.EpicLocalMessage), resource: "TextDim");
            Text(nameof(stores.EpicSessionNotPersistedMessage), nameof(stores.EpicSessionNotPersisted), "Amber");
            Text(nameof(stores.EpicProblemMessage), nameof(stores.ShowEpicProblem), "Amber");
            Add("Sign out of Epic", () => context.ShowActions(stores.EpicSignOutMessage,
                [new("Sign out", () => _ = Run(() => stores.SignOutOfEpicCommand.ExecuteAsync(null), "Couldn't sign out of Epic. Try again.")), new("Cancel", () => { })]), nameof(stores.EpicIsSignedIn));
            var signIn = Add(stores.EpicSignInButtonText,
                () => _ = Run(() => stores.SignInToEpicCommand.ExecuteAsync(null), "Couldn't sign in to Epic. Try again."), nameof(stores.EpicCanSignIn));
            Label(signIn, nameof(stores.EpicSignInButtonText));
            Bind(signIn, IsEnabledProperty, nameof(stores.EpicCanSignIn));
        }
        else { Text(nameof(stores.GogLocalMessage)); Text(nameof(stores.GogNoSignInMessage)); }
        if (platform != "GOG") body.Children.Add(FullscreenUi.Text("The sign-in window supports controller field navigation and text entry. Provider challenges may still ask for a phone or pointer.", 28, "TextDim"));
        body.Children.Add(_notice); Add("Back", context.Back);
        Content = FullscreenUi.Scroll(body); SetFocusRows(controls.ToArray());
        AttachedToVisualTree += (_, _) => PendingRefresh = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var refresh = Context.Shared.Stores.RefreshCommand;
            if (refresh.IsRunning && refresh.ExecutionTask is { } running) await running;
            else await refresh.ExecuteAsync(null);
            if (!_disposed && TopLevel.GetTopLevel(this) is not null) FocusInitial();
        }
        catch (Exception) { if (!_disposed) _notice.Text = "Couldn't read the platform connection status. Reopen this page to try again."; }
    }

    public override void Dispose() { _disposed = true; base.Dispose(); }
}

public sealed class FullscreenSteamConsentPage : FullscreenPage
{
    private readonly Button _cancel;
    private bool _disposed;
    public override string Title => Context.Shared.Stores.SignInConsentTitle;
    public FullscreenSteamConsentPage(FullscreenContext context, Action completed) : base(context)
    {
        var stores = context.Shared.Stores;
        var capture = false;
        var permission = FullscreenUi.Button(stores.CapturePurchaseHistoryLabel + "     Off", () => { });
        permission.Click += (_, _) => { capture = !capture; permission.Content = stores.CapturePurchaseHistoryLabel + (capture ? "     On" : "     Off"); };
        var status = FullscreenUi.Text("", 28, "TextDim");
        _cancel = FullscreenUi.Button(stores.SignInConsentCancelText, context.Back);
        var proceed = FullscreenUi.Button(stores.SignInConsentContinueText, async () =>
        {
            stores.CapturePurchaseHistory = capture;
            status.Text = stores.SteamSignInBusyMessage;
            try
            {
                await stores.SignInToSteamCommand.ExecuteAsync(null);
                await context.RefreshAsync();
                if (!_disposed) { completed(); context.Back(); }
            }
            catch (Exception) { status.Text = "Couldn't sign in to Steam. Try again."; }
        });
        Content = FullscreenUi.Scroll(FullscreenUi.Stack(FullscreenUi.Text(Title, 64), FullscreenUi.Text(stores.SteamSignInGivesMessage),
            FullscreenUi.Text(stores.SteamSignInCostsMessage), FullscreenUi.Text(stores.CapturePurchaseHistoryMessage), permission, status, _cancel, proceed));
        SetFocusRows([permission], [_cancel], [proceed]);
    }
    public override void FocusInitial() => FocusControl(_cancel);
    public override void Dispose() { _disposed = true; Context.Shared.Stores.SignInToSteamCommand.Cancel(); base.Dispose(); }
}

public sealed class FullscreenSteamApiKeyPage : FullscreenPage
{
    private readonly StoresViewModel? _model;
    private readonly TextBox _field;
    public override string Title => "Steam Web API key";
    public FullscreenSteamApiKeyPage(FullscreenContext context) : base(context)
    {
        _model = context.Services is { } services ? ActivatorUtilities.CreateInstance<StoresViewModel>(services) : null;
        var copy = context.Shared.Stores;
        _field = new TextBox { FontSize = 28, MinHeight = 72, PasswordChar = '●', Watermark = copy.SteamApiKeyWatermark };
        var state = FullscreenUi.Text(copy.SteamApiKeyStatusMessage);
        state.Bind(TextBlock.TextProperty, new Binding(nameof(copy.SteamApiKeyStatusMessage)) { Source = copy });
        var edit = FullscreenUi.Button(copy.SteamApiKeyFieldLabel, () => context.EditText(_field));
        var notice = FullscreenUi.Text("", 28, "TextDim");
        var save = FullscreenUi.Button(copy.SteamApiKeySaveButtonText, async () =>
        {
            if (_model is null) { notice.Text = "API key configuration is unavailable."; return; }
            _model.SteamApiKeyInput = _field.Text ?? "";
            if (!_model.SaveSteamApiKeyCommand.CanExecute(null)) { notice.Text = "Enter a Steam Web API key."; return; }
            try
            {
                await _model.SaveSteamApiKeyCommand.ExecuteAsync(null);
                _field.Text = _model.SteamApiKeyInput; notice.Text = _model.SteamApiKeyNoticeMessage;
                await context.Shared.Stores.RefreshCommand.ExecuteAsync(null);
            }
            catch (Exception) { notice.Text = "Couldn't save the API key. Try again."; }
        });
        var clear = FullscreenUi.Button(copy.SteamApiKeyClearButtonText, () => context.ShowActions("Remove the Steam Web API key stored by Winnow?", [new("Remove key", async () =>
        {
            if (_model is null) return;
            try
            {
                await _model.RefreshCommand.ExecuteAsync(null);
                if (!_model.ClearSteamApiKeyCommand.CanExecute(null)) { notice.Text = _model.SteamApiKeyStatusMessage; return; }
                await _model.ClearSteamApiKeyCommand.ExecuteAsync(null); await context.Shared.Stores.RefreshCommand.ExecuteAsync(null);
            }
            catch (Exception) { notice.Text = "Couldn't remove the API key. Try again."; }
        }), new("Cancel", () => { })]));
        var get = FullscreenUi.Button(copy.SteamApiKeyGetButtonText, () => context.Shared.Stores.OpenSteamApiKeyPageCommand.Execute(null));
        var back = FullscreenUi.Button("Back", context.Back);
        Content = FullscreenUi.Scroll(FullscreenUi.Stack(FullscreenUi.Text(Title, 64), FullscreenUi.Text(copy.SteamApiKeyGivesMessage), FullscreenUi.Text(copy.SteamApiKeyCostsMessage),
            state, get, _field, edit, notice, save, clear, back));
        SetFocusRows([get], [_field], [edit], [save], [clear], [back]);
    }
    public override void Dispose() { _field.Text = ""; if (_model is not null) _model.SteamApiKeyInput = ""; base.Dispose(); }
}

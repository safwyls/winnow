using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenSteamConsentPage : FullscreenPage
{
    private readonly Button _cancel;
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
                await context.RefreshAsync(); completed(); context.Back();
            }
            catch (Exception) { status.Text = "Couldn't sign in to Steam. Try again."; }
        });
        Content = FullscreenUi.Scroll(FullscreenUi.Stack(FullscreenUi.Text(Title, 64), FullscreenUi.Text(stores.SteamSignInGivesMessage),
            FullscreenUi.Text(stores.SteamSignInCostsMessage), FullscreenUi.Text(stores.CapturePurchaseHistoryMessage), permission, status, _cancel, proceed));
        SetFocusRows([permission], [_cancel, proceed]);
    }
    public override void FocusInitial() => FocusControl(_cancel);
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
                await context.Shared.Stores.RefreshCommand.ExecuteAsync(null); state.Text = context.Shared.Stores.SteamApiKeyStatusMessage;
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
                await _model.ClearSteamApiKeyCommand.ExecuteAsync(null); await context.Shared.Stores.RefreshCommand.ExecuteAsync(null); state.Text = context.Shared.Stores.SteamApiKeyStatusMessage;
            }
            catch (Exception) { notice.Text = "Couldn't remove the API key. Try again."; }
        }), new("Cancel", () => { })]));
        var get = FullscreenUi.Button(copy.SteamApiKeyGetButtonText, () => context.Shared.Stores.OpenSteamApiKeyPageCommand.Execute(null));
        var back = FullscreenUi.Button("Back", context.Back);
        Content = FullscreenUi.Scroll(FullscreenUi.Stack(FullscreenUi.Text(Title, 64), FullscreenUi.Text(copy.SteamApiKeyGivesMessage), FullscreenUi.Text(copy.SteamApiKeyCostsMessage),
            state, get, _field, edit, notice, save, clear, back));
        SetFocusRows([get], [edit], [save, clear], [back]);
    }
    public override void Dispose() { _field.Text = ""; if (_model is not null) _model.SteamApiKeyInput = ""; base.Dispose(); }
}

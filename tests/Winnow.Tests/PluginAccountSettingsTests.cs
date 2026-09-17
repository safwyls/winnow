using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Tests;

public sealed class PluginAccountSettingsTests
{
    [Fact]
    public async Task Sign_in_waits_for_user_and_poll_interval_then_connects_and_signs_out()
    {
        var time = new Clock();
        var backend = new Backend(time);
        var uris = new Uris();
        var model = new PluginSettingsViewModel(backend, uris, time);
        await model.LoadAsync();
        var card = Assert.Single(model.Plugins);
        Assert.Equal(0, backend.Begins);
        var signIn = card.ConnectCommand.ExecuteAsync(null);
        Assert.True(card.IsConnecting);
        Assert.True(card.HasSignInChallenge);
        Assert.Equal("ABCD-1234", card.UserCode);
        Assert.Null(uris.Opened);
        Assert.False(card.SaveCommand.CanExecute(null));
        Assert.False(card.DisconnectCommand.CanExecute(null));
        await card.OpenSignInPageCommand.ExecuteAsync(null);
        Assert.Equal("https://login.example.com/device", uris.Opened!.AbsoluteUri);
        await time.NextTimerAsync(); // Expiry.
        Assert.Equal(TimeSpan.FromSeconds(5), await time.NextTimerAsync());
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(0, backend.Polls);
        time.Advance(TimeSpan.FromSeconds(1));
        await signIn.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(card.AccountConnected);
        Assert.False(card.IsBusy);
        Assert.False(card.HasSignInChallenge);
        Assert.Empty(card.UserCode);
        Assert.Empty(card.VerificationUrl);
        Assert.Equal(0, backend.Cancels);
        await card.DisconnectCommand.ExecuteAsync(null);
        Assert.False(card.AccountConnected);
        Assert.Equal(1, backend.SignOuts);
    }

    [Fact]
    public async Task Slow_down_increases_interval_and_expiry_clears_pending_attempt()
    {
        var time = new Clock();
        var backend = new Backend(time) { Result = PluginSignInState.SlowDown };
        var model = new PluginSettingsViewModel(backend, timeProvider: time);
        await model.LoadAsync();
        var card = model.Plugins[0];
        var signIn = card.ConnectCommand.ExecuteAsync(null);
        await time.NextTimerAsync();
        await time.NextTimerAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(10), await time.NextTimerAsync());
        Assert.Equal(1, backend.Polls);
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(1, backend.Polls);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(15), await time.NextTimerAsync());
        Assert.Equal(2, backend.Polls);
        time.Advance(TimeSpan.FromMinutes(10));
        await signIn.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("expired", card.AccountStatus);
        Assert.Equal(1, backend.Cancels);
        Assert.Empty(card.UserCode);
        Assert.False(card.AccountConnected);
    }

    [Fact]
    public async Task Leaving_settings_cancels_pending_sign_in_and_ignores_provider_error_text()
    {
        var time = new Clock();
        var backend = new Backend(time) { Result = PluginSignInState.Pending };
        var model = new PluginSettingsViewModel(backend, timeProvider: time);
        await model.LoadAsync();
        var card = model.Plugins[0];
        var signIn = card.ConnectCommand.ExecuteAsync(null);
        model.ClearSecrets();
        Assert.Empty(card.UserCode);
        await signIn.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("cancelled", card.AccountStatus);
        Assert.False(card.IsBusy);
        Assert.Equal(1, backend.Cancels);
        backend.Throw = true;
        await card.ConnectCommand.ExecuteAsync(null);
        Assert.DoesNotContain("private-device-token", card.AccountStatus);
        Assert.Contains("Could not", card.AccountStatus);
    }

    [Fact]
    public async Task Cancelling_while_provider_prepares_challenge_never_displays_late_code()
    {
        var time = new Clock();
        var backend = new Backend(time) { BeginGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var model = new PluginSettingsViewModel(backend, timeProvider: time);
        await model.LoadAsync();
        var card = model.Plugins[0];
        var signIn = card.ConnectCommand.ExecuteAsync(null);
        card.CancelSignInCommand.Execute(null);
        backend.BeginGate.SetResult();
        await signIn.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(card.UserCode);
        Assert.Equal(0, backend.Polls);
        Assert.Equal(1, backend.Cancels);
    }

    [Theory]
    [InlineData("https://untrusted.example.com/device")]
    [InlineData("https://login.example.com.evil.example/device")]
    [InlineData("http://login.example.com/device")]
    [InlineData("https://user:password@login.example.com/device")]
    [InlineData("https://login.example.com:8443/device")]
    public async Task Undeclared_or_unsafe_verification_addresses_are_never_displayed_or_opened(string address)
    {
        var time = new Clock();
        var backend = new Backend(time) { Address = address };
        var uris = new Uris();
        var model = new PluginSettingsViewModel(backend, uris, time);
        await model.LoadAsync();
        var card = model.Plugins[0];
        await card.ConnectCommand.ExecuteAsync(null);
        Assert.Empty(card.VerificationUrl);
        Assert.Empty(card.UserCode);
        Assert.False(card.OpenSignInPageCommand.CanExecute(null));
        Assert.Null(uris.Opened);
        Assert.Equal(0, backend.Polls);
        Assert.Equal(1, backend.Cancels);
    }

    [Fact]
    public async Task Boolean_setting_round_trips_and_new_drafts_survive_a_slow_reload()
    {
        var time = new Clock();
        var backend = new Backend(time);
        var model = new PluginSettingsViewModel(backend);
        await model.LoadAsync();
        var card = model.Plugins[0];
        var field = Assert.Single(card.Fields);
        Assert.True(field.IsBoolean);
        Assert.False(field.IsText);
        Assert.False(field.BooleanValue);
        field.BooleanValue = true;
        await card.SaveCommand.ExecuteAsync(null);
        Assert.Equal("true", backend.SavedValue);
        Assert.True(field.BooleanValue);
        backend.LoadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reload = model.LoadAsync();
        field.BooleanValue = false;
        backend.LoadGate.SetResult();
        await reload;
        Assert.False(field.BooleanValue);
    }

    private sealed class Clock : FakeTimeProvider
    {
        private readonly Channel<TimeSpan> _timers = Channel.CreateUnbounded<TimeSpan>();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _timers.Writer.TryWrite(dueTime);
            return timer;
        }
        public Task<TimeSpan> NextTimerAsync() => _timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Backend(TimeProvider time) : IPluginSettingsBackend
    {
        public string UserPluginDirectory => "plugins";
        public int Begins { get; private set; }
        public int Polls { get; private set; }
        public int Cancels { get; private set; }
        public int SignOuts { get; private set; }
        public bool Throw { get; set; }
        public string Address { get; init; } = "https://login.example.com/device";
        public PluginSignInState Result { get; set; } = PluginSignInState.Connected;
        public string SavedValue { get; private set; } = "false";
        public TaskCompletionSource? BeginGate { get; init; }
        public TaskCompletionSource? LoadGate { get; set; }
        public async Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default)
        {
            if (LoadGate is not null) await LoadGate.Task;
            return [new("xbox", "Xbox", "Played history", "1.0", "Account connection", true, true, false, "",
                [new("include-console", "Include console history", null, false, false, SavedValue, false, IsBoolean: true)],
                HasAccount: true, AccountHosts: ["login.example.com"])];
        }
        public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
        { SavedValue = values["include-console"]; return Task.CompletedTask; }
        public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task RefreshAsync(string pluginId, CancellationToken ct = default) => Task.CompletedTask;
        public async Task<PluginSignInChallenge?> BeginSignInAsync(string pluginId, CancellationToken ct = default)
        {
            Begins++;
            if (Throw) throw new IOException("private-device-token");
            if (BeginGate is not null) await BeginGate.Task;
            return new("opaque-attempt", Address, "ABCD-1234", time.GetUtcNow().AddMinutes(10), 5);
        }
        public Task<PluginSignInResult> PollSignInAsync(string pluginId, string attemptId, CancellationToken ct = default)
        { Polls++; return Task.FromResult(new PluginSignInResult(Result, "private-device-token")); }
        public Task CancelSignInAsync(string pluginId, string attemptId, CancellationToken ct = default)
        { Cancels++; return Task.CompletedTask; }
        public Task SignOutAsync(string pluginId, CancellationToken ct = default)
        { SignOuts++; return Task.CompletedTask; }
    }

    private sealed class Uris : IUriDispatcher
    {
        public Uri? Opened { get; private set; }
        public Task<bool> OpenAsync(Uri uri) { Opened = uri; return Task.FromResult(true); }
    }
}

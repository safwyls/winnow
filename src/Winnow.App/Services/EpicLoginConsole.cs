using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Contracts.Connections;
using Winnow.Core.Auth;

namespace Winnow.App.Services;

public static class EpicLoginConsole
{
    public const string Argument = "--epic-login";
    public const string CodeArgument = "--code";
    public static string? CodeFrom(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == CodeArgument) return i + 1 < args.Count && !args[i + 1].StartsWith('-') ? args[i + 1] : null;
            if (args[i].StartsWith(CodeArgument + "=", StringComparison.Ordinal)) return args[i][(CodeArgument.Length + 1)..];
        }
        return null;
    }

    public static async Task<int> RunAsync(IServiceProvider services, string? presetCode = null, CancellationToken ct = default)
    {
        ConsoleAuthPrompt.AttachConsoleIfNeeded();
        var api = services.GetRequiredService<IStoreConnectionApi>();
        var challenge = await api.BeginEpicAsync(ct);
        try
        {
            var captured = string.IsNullOrWhiteSpace(presetCode)
                ? await new ConsoleAuthPrompt().RequestCodeAsync(challenge.Request, ct)
                : AuthCodeResult.Captured(AuthCodeKind.AuthorizationCode, presetCode, "console");
            if (captured.Outcome != AuthPromptOutcome.Captured) return 1;
            var result = await api.CompleteEpicAsync(challenge, captured, ct);
            Console.WriteLine(result.Succeeded ? "Signed in. The backend is refreshing your Epic library."
                : EpicSignInService.Explain(result.Failure));
            return result.Succeeded ? 0 : 1;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await api.CancelAsync(challenge.AttemptId, cleanup.Token); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException) { }
        }
    }
}

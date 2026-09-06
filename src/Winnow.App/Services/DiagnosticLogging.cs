using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Parsing;

namespace Winnow.App.Services;

internal static class DiagnosticLogging
{
    internal const long FileSizeBytes = 1024 * 1024;
    internal const int RetainedFiles = 5;
    internal const int MaxEventBytes = 8192;

    internal static void Configure(ILoggingBuilder logging, string dataDirectory)
    {
        logging.ClearProviders();
        logging.AddSerilog(Create(dataDirectory), dispose: true);
    }

    internal static Serilog.Core.Logger Create(string dataDirectory, long fileSizeBytes = FileSizeBytes,
        int retainedFiles = RetainedFiles)
        => new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(new DiagnosticFormatter(), Path.Combine(dataDirectory, "logs", "diagnostic.log"),
                fileSizeLimitBytes: fileSizeBytes, rollOnFileSizeLimit: true,
                retainedFileCountLimit: retainedFiles, buffered: false, encoding: new UTF8Encoding(false))
            .CreateLogger();
}

/// <summary>Only the privacy-filtered text reaches the file sink; scopes and arbitrary objects are never rendered.</summary>
internal sealed partial class DiagnosticFormatter : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        var line = new StringBuilder();
        line.Append(logEvent.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Append(' ').Append(logEvent.Level).Append(' ');
        if (logEvent.Properties.TryGetValue("SourceContext", out var context)
            && context is ScalarValue { Value: string category }
            && Category().IsMatch(category))
            line.Append(category).Append(": ");

        // Never render an event wholesale: that would include arbitrary strings,
        // destructured objects, scopes, and exception messages before redaction.
        foreach (var token in logEvent.MessageTemplate.Tokens)
        {
            if (line.Length > DiagnosticLogging.MaxEventBytes) break;
            if (token is TextToken text)
                line.Append(Scrub(text.Text));
            else if (token is PropertyToken property)
                line.Append(SafeValue(property.PropertyName, logEvent));
        }

        var exception = logEvent.Exception;
        for (var i = 0; exception is not null && i < 4; i++, exception = exception.InnerException)
        {
            line.Append(" | ").Append(exception.GetType().Name);
            foreach (var frame in new StackTrace(exception, false).GetFrames().Take(6))
            {
                var method = frame.GetMethod();
                if (method is not null)
                    line.Append(" at ").Append(method.DeclaringType?.FullName).Append('.').Append(method.Name);
            }
        }

        var sanitized = Scrub(line.ToString()).Replace('\r', ' ').Replace('\n', ' ');
        // Serilog checks the file size before writing the next event. Bounding an
        // event makes the maximum overshoot finite even for giant input messages.
        var buffer = new byte[DiagnosticLogging.MaxEventBytes - 1];
        Encoding.UTF8.GetEncoder().Convert(sanitized.AsSpan(), buffer, true,
            out _, out var used, out _);
        output.Write(Encoding.UTF8.GetString(buffer, 0, used));
        output.Write('\n');
    }

    private static string SafeValue(string name, LogEvent logEvent)
    {
        if (IdentityName().IsMatch(name)) return "[redacted]";
        if (!logEvent.Properties.TryGetValue(name, out var value)) return "[missing]";
        if (value is not ScalarValue scalar) return "[redacted]";
        if (name is "Failure" or "ExceptionType" && scalar.Value is string failure && ExceptionType().IsMatch(failure))
            return failure;
        return scalar.Value switch
        {
            null => "null",
            bool boolean => boolean ? "true" : "false",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) ?? "null",
            _ => "[redacted]",
        };
    }

    internal static string Scrub(string text)
    {
        // Static templates normally contain no values. These guards also cover
        // third-party loggers that embed values directly in a message.
        text = text.Length > 32768 ? text[..32768] : text;
        text = PathsAndUrls().Replace(text, "[redacted-path]");
        text = SteamIds().Replace(text, "[redacted-id]");
        text = Credentials().Replace(text, "[redacted-secret]");
        text = Accounts().Replace(text, "[redacted-account]");
        text = Email().Replace(text, "[redacted-account]");
        var username = Environment.UserName;
        if (!string.IsNullOrEmpty(username))
            text = text.Replace(username, "[redacted-user]", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    [GeneratedRegex(@"^(?:Winnow|Microsoft|System)(?:\.[A-Za-z_][A-Za-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Category();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]{0,80}Exception$", RegexOptions.CultureInvariant)]
    private static partial Regex ExceptionType();
    [GeneratedRegex(@"id|account|user|name|path|root|directory|token|secret|password|key|code|credential|authorization|cookie", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IdentityName();
    [GeneratedRegex(@"(?:https?://|file:/{2,3}|[A-Za-z]:[\\/]|\\\\|(?<!\w)/(?:[^\s/]+/)?)[^\r\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex PathsAndUrls();
    [GeneratedRegex(@"\b7656119\d{10}\b|\bSTEAM_[0-5]:[01]:\d+\b|\[U:1:\d+\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SteamIds();
    [GeneratedRegex(@"(?:\b(?:access[_-]?token|refresh[_-]?token|token|api[_-]?key|secret|password|authorization|cookie|code)\b\s*[:=]\s*|\b(?:Bearer|Basic)\s+)[^\r\n]*|\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Credentials();
    [GeneratedRegex(@"\b(?:account|username|user|steam3id|steamid)\s*[:=]\s*[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Accounts();
    [GeneratedRegex(@"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Email();
}

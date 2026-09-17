using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class DiagnosticLoggingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"winnow-diagnostics-{Guid.NewGuid():N}");

    public DiagnosticLoggingTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Startup_fault_is_persisted_without_a_host_even_when_its_normal_sink_is_open(bool openNormalSink)
    {
        using var normal = openNormalSink ? DiagnosticLogging.Create(_root) : null;
        Exception fault;
        try { throw new IOException("unstructured-private-secret"); }
        catch (IOException error) { fault = error; }

        var code = StartupFailure.Report(fault, _root, null, (_, _) => { });

        Assert.Equal(3, code);
        var text = File.ReadAllText(Path.Combine(_root, "logs", "startup-failure.log"));
        Assert.Contains("IOException", text);
        Assert.Contains(nameof(Startup_fault_is_persisted_without_a_host_even_when_its_normal_sink_is_open), text);
        Assert.Contains(fault.HResult.ToString(System.Globalization.CultureInfo.InvariantCulture), text);
        Assert.Contains(" build=", text);
        Assert.DoesNotContain("unstructured-private-secret", text);
        Assert.DoesNotContain(_root, text);
    }

    [Fact]
    public void Failure_to_write_startup_diagnostics_preserves_exit_code_and_alert()
    {
        File.WriteAllText(Path.Combine(_root, "logs"), "Directory deliberately blocked by a file");
        var shown = false;
        Assert.Equal(3, StartupFailure.Report(new IOException("original fault"), _root,
            null, (_, text) => shown = text.Contains("original fault")));
        Assert.True(shown);
    }

    [Fact]
    public void Startup_cancellation_does_not_write_failure_diagnostics()
    {
        Assert.Equal(0, StartupFailure.Report(new OperationCanceledException(), _root,
            null, (_, _) => throw new InvalidOperationException("Unexpected alert")));
        Assert.False(Directory.Exists(Path.Combine(_root, "logs")));
    }

    [Fact]
    public void Host_logging_persists_under_the_selected_data_directory_and_flushes_each_event()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => DiagnosticLogging.Configure(logging, _root));
        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Winnow.Tests.Diagnostics");
        logger.LogWarning("Enrichment retry after {ElapsedMs}ms for {Count} games", 500, 4);

        var text = ReadAll();
        Assert.Contains("Warning Winnow.Tests.Diagnostics", text);
        Assert.Contains("retry after 500ms for 4 games", text);
        Assert.Single(provider.GetServices<ILoggerProvider>());
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "logs")));
    }

    [Fact]
    public void Structured_values_scopes_and_exception_messages_never_reach_disk()
    {
        using var factory = LoggerFactory.Create(logging => DiagnosticLogging.Configure(logging, _root));
        var logger = factory.CreateLogger("Winnow.Tests.Diagnostics");
        using (logger.BeginScope("signed in as {Name} with {Token}", "scope-person", "scope-secret"))
        {
            logger.LogWarning(new IOException("private failure secret", new Exception("inner-private")),
                "Failed for {SteamId} {Steam3Id} {AccountName} {Path} {Secret} {Value} after {ElapsedMs}ms",
                76561197972611406L, 12345678, "private-person", @"C:\Users\private-person\library",
                "private-key", new { User = "nested-private" }, 17);
        }
        var text = ReadAll();
        foreach (var value in new[] { "76561197972611406", "12345678", "private-person", "C:\\Users", "private-key",
                     "nested-private", "scope-person", "scope-secret", "private failure secret", "inner-private" })
            Assert.DoesNotContain(value, text);
        Assert.Contains("IOException", text);
        Assert.Contains("after 17ms", text);
        Assert.Contains("[redacted]", text);
    }

    [Theory]
    [InlineData(@"Unable to read C:\Users\private-person\My Games\game.info", "private-person")]
    [InlineData(@"Unable to read \\private-server\share\game.info", "private-server")]
    [InlineData("Unable to read /home/private-person/My Games/file", "private-person")]
    [InlineData("Request https://example.test/path?key=private-key", "private-key")]
    [InlineData("Steam 76561197972611406 failed", "76561197972611406")]
    [InlineData("Steam STEAM_0:0:6172839 failed", "6172839")]
    [InlineData("Steam [U:1:12345678] failed", "12345678")]
    [InlineData("account = private-person signed in", "private-person")]
    [InlineData("Authorization: Bearer private-token", "private-token")]
    [InlineData("api_key=private-key", "private-key")]
    [InlineData("access_token: private-token", "private-token")]
    [InlineData("Mail private-person@example.test failed", "private-person")]
    public void Unstructured_text_is_scrubbed(string message, string sensitive)
    {
        using var logger = DiagnosticLogging.Create(_root);
        logger.Warning(message);
        Assert.DoesNotContain(sensitive, ReadAll());
    }

    [Fact]
    public void Watcher_operation_is_identifiable_without_allowing_arbitrary_operation_strings()
    {
        using var factory = LoggerFactory.Create(logging => DiagnosticLogging.Configure(logging, _root));
        var logger = factory.CreateLogger("Winnow.Tests.Diagnostics");
        logger.LogWarning("Watcher operation {Operation}", Winnow.Monitor.SessionWatcherOperation.ExecutableIndex);
        logger.LogWarning("Untrusted operation {Operation}", "private-person");
        var text = ReadAll();
        Assert.Contains("Watcher operation ExecutableIndex", text);
        Assert.DoesNotContain("private-person", text);
    }

    [Fact]
    public void Rotation_bounds_file_count_and_bytes_even_for_giant_events_and_restart()
    {
        const int threshold = 256;
        const int retained = 3;
        for (var run = 0; run < 2; run++)
        {
            using var logger = DiagnosticLogging.Create(_root, threshold, retained);
            for (var i = 0; i < 15; i++)
                logger.Warning(new string('x', 20000));
        }
        var files = Directory.GetFiles(Path.Combine(_root, "logs")).Select(path => new FileInfo(path)).ToArray();
        Assert.Equal(retained, files.Length);
        Assert.All(files, file => Assert.InRange(file.Length, 1, threshold + DiagnosticLogging.MaxEventBytes));
        Assert.True(files.Sum(file => file.Length) <= retained * (threshold + DiagnosticLogging.MaxEventBytes));
    }

    [Fact]
    public void New_run_retains_previous_diagnostics()
    {
        using (var logger = DiagnosticLogging.Create(_root)) logger.Warning("first-run diagnostic");
        using (var logger = DiagnosticLogging.Create(_root)) logger.Warning("second-run diagnostic");
        Assert.Contains("first-run diagnostic", ReadAll());
        Assert.Contains("second-run diagnostic", ReadAll());
    }

    [Fact]
    public void Every_event_identifies_the_build_and_run_even_after_rotation()
    {
        using (var logger = DiagnosticLogging.Create(_root, fileSizeBytes: 256))
        {
            logger.Warning("first diagnostic");
            logger.Warning("second diagnostic");
        }
        var firstRun = ReadAll();
        var identifiers = Regex.Matches(firstRun, @"run=([a-f0-9]{32})");
        Assert.Equal(2, identifiers.Count);
        Assert.Equal(identifiers[0].Groups[1].Value, identifiers[1].Groups[1].Value);
        foreach (var line in firstRun.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.Contains(" build=", line);
            Assert.Contains(" commit=", line);
            Assert.Contains(" os=", line);
            Assert.Contains(" arch=", line);
            Assert.Contains(" runtime=", line);
        }

        using (var logger = DiagnosticLogging.Create(_root)) logger.Warning("new run diagnostic");
        Assert.Equal(2, Regex.Matches(ReadAll(), @"run=([a-f0-9]{32})")
            .Select(match => match.Groups[1].Value).Distinct().Count());
    }

    private string ReadAll() => string.Join("\n", Directory.GetFiles(Path.Combine(_root, "logs"))
        .Select(path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }));
}

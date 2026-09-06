using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class DiagnosticLoggingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"winnow-diagnostics-{Guid.NewGuid():N}");

    public DiagnosticLoggingTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

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

    private string ReadAll() => string.Join("\n", Directory.GetFiles(Path.Combine(_root, "logs"))
        .Select(path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }));
}

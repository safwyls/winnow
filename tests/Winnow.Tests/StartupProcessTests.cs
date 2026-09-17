using System.Diagnostics;
using Dapper;
using Winnow.App.ViewModels;
using Winnow.Data;
using Xunit;

namespace Winnow.Tests;

public sealed class StartupProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_startup_opens_a_responsive_native_window(bool fullscreen)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new StartupDirectory();
        fixture.CreateLibrary(fullscreen);
        var start = CreateStartInfo(fixture);
        // WaitForInputIdle requires a GUI-subsystem executable, rather than dotnet.exe.
        start.FileName = Path.Combine(AppContext.BaseDirectory, "Winnow.exe");
        start.ArgumentList.RemoveAt(0);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!process.HasExited && process.MainWindowHandle == IntPtr.Zero)
            {
                await Task.Delay(100, timeout.Token);
                process.Refresh();
            }
            Assert.False(process.HasExited, "Winnow exited before opening its native window.");
            Assert.True(process.WaitForInputIdle(10_000), "The native message loop never became idle.");
            // A window can be created before MainLoop throws; give that failure time to surface.
            await Task.Delay(1000, timeout.Token);
            process.Refresh();
            Assert.False(process.HasExited, "Winnow exited after creating its native window.");
            Assert.True(process.Responding, "The native window is not processing messages.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await output;
        }
        Assert.DoesNotContain("Winnow could not start", await error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_configuration_is_reported_before_the_library_is_opened(bool fullscreen)
    {
        using var fixture = new StartupDirectory();
        fixture.CreateLibrary(fullscreen);
        var before = File.ReadAllBytes(fixture.DatabasePath);
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), "{ invalid-json");

        var result = await LaunchAsync(fixture);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Winnow could not start", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(fixture.DatabasePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_host_logging_configuration_is_reported_without_starting_the_ui(bool fullscreen)
    {
        using var fixture = new StartupDirectory();
        fixture.CreateLibrary(fullscreen);
        var before = File.ReadAllBytes(fixture.DatabasePath);
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"),
            """{"Logging":{"LogLevel":{"Default":"invalid-review-level"}}}""");

        var result = await LaunchAsync(fixture);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Winnow could not start", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(fixture.DatabasePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsupported_schema_is_refused_before_services_or_either_surface_start(bool fullscreen)
    {
        using var fixture = new StartupDirectory();
        var factory = fixture.CreateLibrary(fullscreen);
        var path = fixture.DatabasePath;
        using (var connection = factory.Open())
        {
            connection.Execute("""
                INSERT INTO SchemaVersions (ScriptName, Applied)
                VALUES ('Winnow.Data.Migrations.9999_future_schema.sql', '2026-09-11 00:00:00');
                """);
            connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        }
        var before = File.ReadAllBytes(path);

        var result = await LaunchAsync(fixture);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("does not support", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Unusable_data_directory_still_returns_exit_two()
    {
        using var fixture = new StartupDirectory();
        File.WriteAllText(fixture.Data, "a file cannot be the data directory");

        var result = await LaunchAsync(fixture);

        Assert.Equal(2, result.ExitCode);
    }

    private static async Task<(int ExitCode, string Error)> LaunchAsync(StartupDirectory fixture)
    {
        using var process = Process.Start(CreateStartInfo(fixture))!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("The isolated startup failure did not exit within 30 seconds.");
        }
        await output;
        return (process.ExitCode, await error);
    }

    private static ProcessStartInfo CreateStartInfo(StartupDirectory fixture)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = fixture.Root,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Winnow.dll"));
        start.ArgumentList.Add("--data-dir");
        start.ArgumentList.Add(fixture.Data);
        start.ArgumentList.Add("--no-sync");
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        return start;
    }

    private sealed class StartupDirectory : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"winnow-startup-{Guid.NewGuid():N}");
        public string Data => Path.Combine(Root, "data");
        public string DatabasePath => Path.Combine(Data, "winnow.db");
        public StartupDirectory() => Directory.CreateDirectory(Root);

        public SqliteConnectionFactory CreateLibrary(bool fullscreen)
        {
            Directory.CreateDirectory(Data);
            var factory = new SqliteConnectionFactory(DatabasePath, pooling: false);
            new DatabaseInitializer(factory).Initialize();
            using var connection = factory.Open();
            connection.Execute("INSERT INTO settings (key, value) VALUES (@key, @value);",
                new { key = ApplicationSettingsViewModel.StartInFullscreenSettingKey, value = fullscreen ? "true" : "false" });
            connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            return factory;
        }

        public void Dispose()
        {
            // Native process teardown can release SQLite handles shortly after exit is signalled.
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Delete(Root, recursive: true); return; }
                catch (IOException) when (attempt < 20) { Thread.Sleep(100); }
            }
        }
    }
}

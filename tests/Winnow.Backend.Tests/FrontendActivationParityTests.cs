using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Winnow.Activation;
using Winnow.App.Services;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class FrontendActivationParityTests
{
    [Fact]
    public async Task Real_helper_owns_only_the_frontend_and_forwards_exact_Int64_requests_then_EOF_releases_the_guard()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Scratch();
        try
        {
            using var avalonia = SingleInstanceGuard.TryAcquire(root);
            Assert.NotNull(avalonia);
            await using (var primary = await Helper.Start(root))
            {
                Assert.Equal("primary", primary.First.GetProperty("kind").GetString());
                Assert.Equal(Environment.ProcessId, primary.First.GetProperty("parentProcessId").GetInt32());
                Assert.Equal(primary.Process.Id, primary.First.GetProperty("processId").GetInt32());
                Assert.False(File.Exists(Path.Combine(root, "winnow.db")));
                Assert.Empty(Directory.GetFiles(root));
                using var denied = SingleInstanceGuard.TryAcquire(root, "Electron");
                Assert.Null(denied);
                foreach (var value in new[] { "42", "9007199254740993", "9223372036854775807" })
                {
                    await using var secondary = await Helper.Start(root.ToUpperInvariant() + Path.DirectorySeparatorChar,
                        JsonSerializer.Serialize(new { version = 1, activation = new { kind = "game", ownershipId = value } }));
                    Assert.Equal("forwarded", secondary.First.GetProperty("kind").GetString());
                    Assert.True(secondary.First.GetProperty("accepted").GetBoolean());
                    var activation = await primary.Next();
                    Assert.Equal(value, activation.GetProperty("activation").GetProperty("ownershipId").GetString());
                    await secondary.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal(0, secondary.Process.ExitCode);
                }
                await using var other = await Helper.Start(root + "-other");
                Assert.Equal("primary", other.First.GetProperty("kind").GetString());
            }
            await using var reacquired = await Helper.Start(root);
            Assert.Equal("primary", reacquired.First.GetProperty("kind").GetString());
        }
        finally { Directory.Delete(root, true); if (Directory.Exists(root + "-other")) Directory.Delete(root + "-other", true); }
    }

    [Fact]
    public async Task Actual_helper_kernel_objects_have_current_SID_owner_protected_network_deny_and_only_current_user_allow()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Scratch();
        try
        {
            await using var helper = await Helper.Start(root);
            using var identity = WindowsIdentity.GetCurrent();
            using var mutex = Mutex.OpenExisting(helper.First.GetProperty("mutexName").GetString()!);
            using var pipe = new NamedPipeClientStream(".", helper.First.GetProperty("pipeName").GetString()!, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000);
            foreach (var security in new NativeObjectSecurity[] { mutex.GetAccessControl(), pipe.GetAccessControl() })
            {
                Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
                Assert.True(security.AreAccessRulesProtected);
                var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<AccessRule>().ToArray();
                Assert.Equal(2, rules.Length);
                Assert.Equal(AccessControlType.Deny, rules[0].AccessControlType);
                Assert.Equal(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), rules[0].IdentityReference);
                Assert.Equal(AccessControlType.Allow, rules[1].AccessControlType);
                Assert.Equal(identity.User, rules[1].IdentityReference);
            }
            var pid = new byte[4];
            await pipe.ReadExactlyAsync(pid);
            Assert.Equal(Environment.ProcessId, BitConverter.ToInt32(pid));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Spoofed_parent_is_refused_before_ownership_or_database_creation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Scratch();
        try
        {
            using var process = Helper.Launch(root, parent: 1);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, process.ExitCode);
            Assert.Contains("parent does not match", await process.StandardError.ReadToEndAsync());
            Assert.Empty(Directory.GetFiles(root));
            using var guard = SingleInstanceGuard.TryAcquire(root, "Electron");
            Assert.NotNull(guard);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Loss_of_the_real_parent_releases_the_helper_and_allows_a_new_owner()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Scratch();
        var ready = root + ".parent.json";
        Process? parent = null;
        try
        {
            static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
            var script = $$$"""
                $p = New-Object System.Diagnostics.ProcessStartInfo
                $p.FileName = 'dotnet'
                $p.UseShellExecute = $false
                $p.CreateNoWindow = $true
                $p.RedirectStandardInput = $true
                $p.RedirectStandardOutput = $true
                $p.RedirectStandardError = $true
                $p.Arguments = '"' + {{{Quote(typeof(BackendApplication).Assembly.Location)}}} + '" --frontend-activation-helper --parent-pid ' + $PID + ' --data-dir "' + {{{Quote(root)}}} + '"'
                $child = [System.Diagnostics.Process]::Start($p)
                $child.StandardInput.WriteLine('{"version":1,"activation":{"kind":"show"}}')
                $child.StandardInput.Flush()
                [System.IO.File]::WriteAllText({{{Quote(ready)}}}, $child.StandardOutput.ReadLine())
                Start-Sleep -Seconds 60
                """;
            var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) info.ArgumentList.Add(value);
            parent = Process.Start(info)!;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(ready) && DateTime.UtcNow < deadline) await Task.Delay(25);
            Assert.True(File.Exists(ready));
            using var frame = JsonDocument.Parse(await File.ReadAllTextAsync(ready));
            using var helper = Process.GetProcessById(frame.RootElement.GetProperty("processId").GetInt32());
            Assert.Equal(parent.Id, frame.RootElement.GetProperty("parentProcessId").GetInt32());
            parent.Kill();
            await parent.WaitForExitAsync();
            await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, helper.ExitCode);
            await using var replacement = await Helper.Start(root);
            Assert.Equal("primary", replacement.First.GetProperty("kind").GetString());
            Assert.Empty(Directory.GetFiles(root));
        }
        finally
        {
            if (parent is { HasExited: false }) parent.Kill();
            parent?.Dispose();
            File.Delete(ready);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Shared_data_location_operation_resolves_the_isolated_root_without_constructing_a_backend()
    {
        var root = Scratch();
        try
        {
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { typeof(BackendApplication).Assembly.Location, "--resolve-data-location", "--data-dir", root }) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, process.ExitCode);
            using var result = JsonDocument.Parse(await output);
            Assert.Equal(root, result.RootElement.GetProperty("root").GetString());
            Assert.Empty(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("{\"version\":1,\"activation\":{\"kind\":\"game\",\"ownershipId\":42}}")]
    [InlineData("{\"version\":1,\"activation\":{\"kind\":\"game\",\"ownershipId\":\"042\"}}")]
    [InlineData("{\"version\":1,\"activation\":{\"kind\":\"game\",\"ownershipId\":\"9223372036854775808\"}}")]
    [InlineData("{\"version\":1,\"activation\":{\"kind\":\"plugin\",\"pluginId\":[],\"releaseTag\":\"v1.0.0\"}}")]
    [InlineData("{\"version\":1,\"activation\":{\"kind\":\"plugin\",\"pluginId\":\"xbox&release=v1.0.0\",\"releaseTag\":\"v2.0.0\"}}")]
    [InlineData("{\"version\":1,\"activation\":{\"kind\":\"show\",\"other\":true}}")]
    public void Typed_protocol_rejects_coercion_overflow_delimiters_and_extra_fields(string frame)
        => Assert.ThrowsAny<Exception>(() => FrontendActivationHost.DecodeRequest(Encoding.UTF8.GetBytes(frame)));

    [Fact]
    public async Task Protocol_enforces_frame_boundaries_before_parsing_and_preserves_plugin_fields()
    {
        var frame = Encoding.UTF8.GetBytes("{\"version\":1,\"activation\":{\"kind\":\"plugin\",\"pluginId\":\"xbox\",\"releaseTag\":\"v1.2.3-beta.4\"}}\n");
        var decoded = FrontendActivationHost.DecodeRequest((await FrontendActivationHost.ReadFrameAsync(new MemoryStream(frame), default))!);
        Assert.Equal("xbox", decoded.Plugin!.PluginId);
        Assert.Equal("v1.2.3-beta.4", decoded.Plugin.ReleaseTag);
        await Assert.ThrowsAsync<InvalidDataException>(() => FrontendActivationHost.ReadFrameAsync(new MemoryStream(new byte[2049]), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => FrontendActivationHost.ReadFrameAsync(new MemoryStream([1]), default));
        Assert.Null(await FrontendActivationHost.ReadFrameAsync(new MemoryStream(), default));
    }

    private static string Scratch() { var root = Path.Combine(Path.GetTempPath(), "winnow-activation-contract-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    private sealed class Helper : IAsyncDisposable
    {
        public required Process Process { get; init; }
        public JsonElement First { get; private set; }
        public static Process Launch(string root, int? parent = null)
        {
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(typeof(BackendApplication).Assembly.Location);
            foreach (var value in new[] { FrontendActivationHost.Argument, "--parent-pid", (parent ?? Environment.ProcessId).ToString(), "--data-dir", root }) info.ArgumentList.Add(value);
            return Process.Start(info)!;
        }
        public static async Task<Helper> Start(string root, string frame = "{\"version\":1,\"activation\":{\"kind\":\"show\"}}")
        {
            var helper = new Helper { Process = Launch(root) };
            try
            {
                await helper.Process.StandardInput.WriteLineAsync(frame);
                await helper.Process.StandardInput.FlushAsync();
                helper.First = await helper.Next();
                return helper;
            }
            catch { await helper.DisposeAsync(); throw; }
        }
        public async Task<JsonElement> Next()
        {
            var line = await Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (line is null) throw new InvalidOperationException(await Process.StandardError.ReadToEndAsync());
            return JsonDocument.Parse(line).RootElement.Clone();
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                Process.StandardInput.Close();
                await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(0, Process.ExitCode);
            }
            finally { if (!Process.HasExited) Process.Kill(); Process.Dispose(); }
        }
    }
}

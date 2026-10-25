using System.Collections.Concurrent;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.App.Services;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Monitor;

namespace Winnow.Electron.Fixtures;

internal sealed class DiagnosticsFixture(ISqliteConnectionFactory database,
    IProgress<EnrichmentProgress> progress, SessionWatcherHealth health)
{
    private readonly ConcurrentDictionary<string, int> _requests = new();
    private readonly IProgress<EnrichmentProgress> _progress = progress;
    private readonly SessionWatcherHealth _health = health;
    private DiagnosticsProgress _current = new(0, 0);
    private string _root = "";

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<DiagnosticsFixture>();
        services.RemoveAll<ICoverSource>();
        services.RemoveAll<IIgdbCredentialProvider>();
        services.AddSingleton<IIgdbCredentialProvider, StartupNoCredentials>();
    }

    public void Initialize(string directory)
    {
        _root = directory;
        using var connection = database.Open();
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
            throw new InvalidOperationException("Diagnostics fixtures require an empty library.");
        connection.Execute("""
            INSERT INTO works(id,name,sort_name) VALUES(1,'Diagnostics fixture','Diagnostics fixture');
            INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Diagnostics fixture','windows');
            INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'manual',0);
            INSERT OR REPLACE INTO settings(key,value) VALUES('appearance.theme','winnow');
            INSERT OR REPLACE INTO settings(key,value) VALUES('application.start_in_fullscreen',@mode);
            INSERT OR REPLACE INTO settings(key,value) VALUES('fullscreen.reduced-motion','true');
            """, new { mode = Path.GetFileName(directory).Contains("-fullscreen-", StringComparison.Ordinal) ? "true" : "false" });
    }

    public static void Map(WebApplication app)
    {
        var fixture = app.Services.GetRequiredService<DiagnosticsFixture>();
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/api/v1/", StringComparison.Ordinal) && context.Request.Method == "GET")
                fixture._requests.AddOrUpdate(path, 1, (_, count) => count + 1);
            await next(context);
        });
        app.MapGet("/__fixture/diagnostics/state", () => new
        {
            Root = fixture._root, LogsDirectory = Path.Combine(fixture._root, "logs"),
            Requests = fixture._requests.ToArray().ToDictionary(entry => entry.Key, entry => entry.Value),
            Progress = Volatile.Read(ref fixture._current), Failures = fixture._health.Failures,
            ProcessId = Environment.ProcessId
        });
        app.MapPost("/__fixture/diagnostics/progress", (DiagnosticsProgress input) =>
        {
            Volatile.Write(ref fixture._current, input);
            fixture._progress.Report(new EnrichmentProgress(input.Total, input.Remaining));
            return Results.NoContent();
        });
        app.MapPost("/__fixture/diagnostics/health", (DiagnosticsHealth input) =>
        {
            if (input.Failure) fixture._health.ReportFailure(SessionWatcherOperation.ExecutableIndex, new IOException("private fixture failure"));
            else fixture._health.ReportSuccess(SessionWatcherOperation.ExecutableIndex);
            return Results.NoContent();
        });
    }
}

internal sealed record DiagnosticsProgress(int Total, int Remaining);
internal sealed record DiagnosticsHealth(bool Failure);

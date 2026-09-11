using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class MetadataWriteBatchTests
{
    public static TheoryData<string, string, bool> FailurePoints
    {
        get
        {
            var cases = new TheoryData<string, string, bool>();
            foreach (var (operation, point) in new[]
            {
                ("set", "BEFORE UPDATE ON works"),
                ("set", "BEFORE INSERT ON work_field_sources"),
                ("reset", "BEFORE UPDATE ON works"),
                ("reset", "BEFORE DELETE ON work_field_sources"),
                ("pin", "BEFORE UPDATE ON work_igdb_pins"),
                ("pin", "BEFORE INSERT ON work_igdb_pins"),
                ("pin", "BEFORE UPDATE ON works"),
                ("pin", "BEFORE INSERT ON work_field_sources WHEN NEW.field = 'publisher'"),
                ("enrich", "BEFORE UPDATE ON works"),
                ("enrich", "BEFORE INSERT ON work_field_sources WHEN NEW.field = 'publisher'"),
                ("plugin-enrich", "BEFORE INSERT ON work_field_sources WHEN NEW.field = 'publisher'"),
            })
            {
                cases.Add(operation, point, false);
                cases.Add(operation, point, true);
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(FailurePoints))]
    public async Task Failure_at_a_dependent_write_preserves_the_entire_previous_state(
        string operation, string point, bool ambient)
    {
        using var db = Seed(operation);
        var before = Snapshot(db);
        using (var connection = db.Factory.Open())
        {
            connection.Execute($"""
                CREATE TRIGGER reject_metadata {point}
                BEGIN SELECT RAISE(ABORT, 'injected metadata failure'); END;
                """);
        }

        using (var outer = ambient ? db.Factory.Begin() : null)
        {
            await Assert.ThrowsAsync<SqliteException>(() => Write(db.Factory, operation));
            if (ambient)
            {
                await new SettingsRepository(db.Factory).SetAsync("other_operation", "committed");
                outer!.Commit();
            }
        }

        Assert.Equal(before, Snapshot(db));
        if (ambient)
        {
            Assert.Equal("committed", await new SettingsRepository(db.Factory).GetAsync("other_operation"));
        }
    }

    [Theory]
    [InlineData("set", false)]
    [InlineData("set", true)]
    [InlineData("reset", false)]
    [InlineData("reset", true)]
    [InlineData("pin", false)]
    [InlineData("pin", true)]
    [InlineData("enrich", false)]
    [InlineData("enrich", true)]
    [InlineData("plugin-enrich", false)]
    [InlineData("plugin-enrich", true)]
    public async Task A_successful_operation_leaves_the_outer_commit_decision_to_its_caller(
        string operation, bool commit)
    {
        using var db = Seed(operation);
        var before = Snapshot(db);

        using (var outer = db.Factory.Begin())
        {
            await Write(db.Factory, operation);
            if (commit)
            {
                outer.Commit();
            }
        }

        if (commit)
        {
            Assert.NotEqual(before, Snapshot(db));
        }
        else
        {
            Assert.Equal(before, Snapshot(db));
        }
    }

    [Theory]
    [InlineData("set", false)]
    [InlineData("set", true)]
    [InlineData("reset", false)]
    [InlineData("reset", true)]
    [InlineData("pin", false)]
    [InlineData("pin", true)]
    [InlineData("enrich", false)]
    [InlineData("enrich", true)]
    public async Task Cancellation_after_the_value_write_rolls_back_values_sources_and_pins(
        string operation, bool ambient)
    {
        using var db = Seed(operation);
        var before = Snapshot(db);
        using var cancellation = new CancellationTokenSource();
        var factory = new CancellingFactory(db.Factory, cancellation);
        using (var connection = db.Factory.Open())
        {
            connection.Execute("""
                CREATE TRIGGER cancel_metadata AFTER UPDATE ON works
                BEGIN SELECT cancel_metadata_write(); END;
                """);
        }

        using (var outer = ambient ? factory.Begin() : null)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Write(factory, operation, cancellation.Token));
            outer?.Commit();
        }

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(before, Snapshot(db));
    }

    private static async Task Write(ISqliteConnectionFactory factory, string operation, CancellationToken ct = default)
    {
        switch (operation)
        {
            case "set":
                Assert.Equal(WorkFieldEditOutcome.Applied,
                    await new WorkFieldSourceRepository(factory).SetFieldAsync(1, WorkFields.Summary, "User value", ct));
                break;
            case "reset":
                Assert.Equal(WorkFieldEditOutcome.Applied,
                    await new WorkFieldSourceRepository(factory).ResetFieldAsync(1, WorkFields.Summary, ct));
                break;
            case "pin":
                Assert.Equal(WorkIgdbPinOutcome.Pinned, await new WorkIgdbPinRepository(factory).PinAsync(
                    new WorkIgdbPinAssignment
                    {
                        WorkId = 1, IgdbId = 222, Name = "New identity", FirstReleaseYear = 2020,
                        Summary = "New summary", Publisher = "New publisher",
                    }, ct));
                break;
            default:
                await new WorkRepository(factory).ApplyEnrichmentAsync(new WorkEnrichment(
                    1, Name: "Enriched", FirstReleaseYear: 2020, Summary: "Automatic", Publisher: "Publisher")
                {
                    Source = operation == "plugin-enrich" ? "plugin:review" : FieldSources.Igdb,
                }, ct);
                break;
        }
    }

    private static TempDatabase Seed(string operation)
    {
        var db = new TempDatabase();
        using var connection = db.Factory.Open();
        connection.Execute("INSERT INTO works(id,name,name_is_provisional) VALUES(1,'Original',1);");
        if (operation == "reset")
        {
            connection.Execute("""
                UPDATE works SET summary = 'User value' WHERE id=1;
                INSERT INTO work_field_sources(work_id,field,source,set_at)
                VALUES(1,'summary','user','2026-09-10 00:00:00');
                """);
        }
        else if (operation == "pin")
        {
            connection.Execute("""
                UPDATE works SET igdb_id = 111 WHERE id=1;
                INSERT INTO work_igdb_pins(work_id,igdb_id,pinned_at)
                VALUES(1,111,'2026-09-10 00:00:00');
                """);
        }

        return db;
    }

    private static string Snapshot(TempDatabase db)
    {
        using var connection = db.Factory.Open();
        return string.Join('\n', connection.Query<string>("""
            SELECT json_object('igdb',igdb_id,'revision',igdb_mapping_revision,'name',name,'provisional',name_is_provisional,
                               'year',first_release_year,'summary',summary,'publisher',publisher)
            FROM works ORDER BY id;
            """)) + "\n" + string.Join('\n', connection.Query<string>("""
            SELECT json_object('work',work_id,'field',field,'source',source,'set',set_at)
            FROM work_field_sources ORDER BY work_id,field;
            """)) + "\n" + string.Join('\n', connection.Query<string>("""
            SELECT json_object('id',id,'work',work_id,'igdb',igdb_id,'pinned',pinned_at,'cleared',cleared_at)
            FROM work_igdb_pins ORDER BY id;
            """));
    }

    private sealed class CancellingFactory(ISqliteConnectionFactory inner, CancellationTokenSource cancellation)
        : ISqliteConnectionFactory
    {
        public string DatabasePath => inner.DatabasePath;
        public string ConnectionString => inner.ConnectionString;
        public IUnitOfWork Begin() => inner.Begin();
        public void ReleasePooledConnections() => inner.ReleasePooledConnections();

        public SqliteConnection Open()
        {
            var connection = inner.Open();
            Configure(connection);
            return connection;
        }

        public DbLease Lease()
        {
            var lease = inner.Lease();
            Configure(lease.Connection);
            return lease;
        }

        private void Configure(SqliteConnection connection)
            => connection.CreateFunction("cancel_metadata_write", () => { cancellation.Cancel(); return 0; });
    }
}

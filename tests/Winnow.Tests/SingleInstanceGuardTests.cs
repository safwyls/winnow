using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// TASK-23 (F39), the guard half: one Winnow per data directory. The mutex is
/// named after the directory, so the same files are refused and a different
/// throwaway <c>--data-dir</c> is not.
/// </summary>
public class SingleInstanceGuardTests
{
    /// <summary>A directory nobody else is using: a unique name per test, because
    /// named mutexes are global to the machine and test classes run in parallel.</summary>
    private static string FreshDirectory()
        => Path.Combine(Path.GetTempPath(), "winnow-guard-", Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_first_copy_acquires_and_a_second_copy_on_the_same_directory_is_refused()
    {
        var directory = FreshDirectory();

        using var first = SingleInstanceGuard.TryAcquire(directory);
        Assert.NotNull(first);

        // Held, not merely created: a null here is the "second copy shows a
        // sentence and exits" arm, so it is the whole behaviour under test.
        using var second = SingleInstanceGuard.TryAcquire(directory);
        Assert.Null(second);
    }

    [Fact]
    public void Releasing_the_guard_lets_the_next_launch_through()
    {
        var directory = FreshDirectory();

        var first = SingleInstanceGuard.TryAcquire(directory);
        Assert.NotNull(first);
        first!.Dispose();

        using var second = SingleInstanceGuard.TryAcquire(directory);
        Assert.NotNull(second);
    }

    [Fact]
    public void A_second_copy_against_a_different_data_directory_is_not_the_failure()
    {
        using var first = SingleInstanceGuard.TryAcquire(FreshDirectory());
        Assert.NotNull(first);

        // --data-dir at a throwaway path is the documented safe way to run a
        // second copy, and the guard must not refuse it.
        using var second = SingleInstanceGuard.TryAcquire(FreshDirectory());
        Assert.NotNull(second);
    }

    [Fact]
    public void Two_spellings_of_one_directory_are_still_one_instance()
    {
        var directory = FreshDirectory();
        var withTrailingSeparator = directory + Path.DirectorySeparatorChar;

        using var first = SingleInstanceGuard.TryAcquire(directory);
        Assert.NotNull(first);

        using var second = SingleInstanceGuard.TryAcquire(withTrailingSeparator);
        Assert.Null(second);
    }
}
